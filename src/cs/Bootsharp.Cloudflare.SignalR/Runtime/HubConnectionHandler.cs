using System.Buffers;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// Reimplementation of upstream's <c>HubConnectionHandler&lt;THub&gt;</c> + the message half of
/// <c>DefaultHubDispatcher</c>, at roughly an eighth of the size: no <c>ConnectionContext</c>, no
/// <c>PipeReader</c> loop, no write lock, no heartbeat. What is preserved exactly is the
/// <b>order of operations</b> — of <c>HandshakeAsync</c> (<c>HubConnectionContext.cs:603-760</c>)
/// and of <c>DispatchMessageAsync</c>'s switch (<c>DefaultHubDispatcher.cs:199-280</c>), which is
/// the behavioural spec names.
/// </summary>
/// <remarks>
/// The whole class is driven by discrete events rather than a loop, because a hibernation socket
/// outlives the isolate: <see cref="AcceptAsync"/> on the 101, <see cref="ReceiveAsync"/> per frame,
/// <see cref="DisconnectAsync"/> on close, <see cref="SweepAsync"/> on the alarm.
/// </remarks>
public sealed class HubConnectionHandler<THub> where THub : Hub
{
    private readonly HubDispatcher<THub> dispatcher;
    private readonly DurableObjectHubLifetimeManager<THub> lifetime;
    private readonly IConnectionTransport transport;
    private readonly IHubProtocol protocol;
    private readonly HubOptions options;
    private readonly ConnectionDispatchQueue queue = new();
    private readonly InvocationBinder binder;
    private readonly Func<double> now;

    public HubConnectionHandler (
        HubDispatcher<THub> dispatcher,
        IConnectionTransport transport,
        HubOptions? options = null,
        Func<double>? clock = null)
    {
        this.dispatcher = dispatcher;
        this.transport = transport;
        this.options = options ?? new HubOptions();
        protocol = this.options.CreateProtocol();
        lifetime = new DurableObjectHubLifetimeManager<THub>(transport, protocol);
        binder = new InvocationBinder(dispatcher);
        now = clock ?? (static () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Context = new HubContext<THub>(lifetime);
    }

    /// <summary>The lifetime manager, so an app can reach <c>IHubContext</c> outside a hub method.</summary>
    public HubLifetimeManager<THub> Lifetime => lifetime;

    /// <summary>An <c>IHubContext&lt;THub&gt;</c> for code with no calling connection.</summary>
    public IHubContext<THub> Context { get; }

    /// <summary>
    /// Registers a freshly accepted socket. Nothing is sent: the client speaks first, and the very
    /// first frame it receives must be the handshake response (<c>HubConnection.ts:647-655</c>).
    /// </summary>
    public Task AcceptAsync (string connectionId)
    {
        var state = new ConnectionState { AcceptedAt = now() };
        transport.Attach(connectionId, state.Serialize());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles one received text frame. Chained behind whatever is already in flight for this
    /// connection — see <see cref="ConnectionDispatchQueue"/> for the measurement that makes the
    /// chain mandatory rather than defensive.
    /// </summary>
    public Task ReceiveAsync (string connectionId, string frame) =>
        queue.Enqueue(connectionId, () => ReceiveCoreAsync(connectionId, frame));

    private async Task ReceiveCoreAsync (string connectionId, string frame)
    {
        var buffer = HubFraming.AsSequence(frame);
        var state = ConnectionState.Deserialize(transport.Attachment(connectionId));
        if (!state.Handshaken)
        {
            if (!Handshake(connectionId, state, ref buffer)) return;
            await AfterHandshake(connectionId, state).ConfigureAwait(false);
            // The handshake frame may carry trailing messages: parseHandshakeResponse splits at the
            // first 0x1E and hands the remainder to the normal parser, so batching is legal in both
            // directions (HandshakeProtocol.ts:26-68) and dropping the tail would lose an
            // invocation the client considers sent.
            if (buffer.IsEmpty) return;
        }
        if (!lifetime.TryGetConnection(connectionId, out var connection))
        {
            // A hibernation wake: the socket survived, the isolate did not. Everything durable is in
            // the attachment, so the context is rebuilt rather than the connection dropped.
            connection = Rebuild(connectionId, state);
            await lifetime.OnConnectedAsync(connection).ConfigureAwait(false);
        }
        while (protocol.TryParseMessage(ref buffer, binder, out var message))
            await DispatchAsync(connection, message).ConfigureAwait(false);
    }

    /// <summary>
    /// Upstream's <c>HandshakeAsync</c> order of operations, minus the parts whose mechanism does
    /// not exist here: resolve protocol → version check → apply user identity → <b>then</b> write
    /// the response. The transfer-format check is gone because there is no
    /// <c>ITransferFormatFeature</c> to disagree with (a workerd text frame is Text, always); the
    /// keepalive registration is gone because the auto-responder replaced it.
    /// </summary>
    /// <remarks>
    /// Synchronous by construction, and it has to be: it advances <paramref name="buffer"/> past the
    /// handshake so the caller can parse the messages the same frame may carry after it, and a
    /// <c>ref</c> sequence cannot cross an <c>await</c>. That is not a constraint being worked
    /// around — nothing in the handshake needs to await, and the callbacks that do
    /// (<see cref="AfterHandshake"/>) run once the response is already on the wire, which is
    /// upstream's order too.
    /// </remarks>
    private bool Handshake (string connectionId, ConnectionState state, ref ReadOnlySequence<byte> buffer)
    {
        HandshakeRequestMessage request;
        try
        {
            if (!HandshakeProtocol.TryParseRequestMessage(ref buffer, out request!))
            {
                // No terminator. The client has no partial-frame buffer and neither does workerd's
                // delivery: one frame is one message, so an incomplete handshake frame is a
                // malformed one rather than a partial read to be continued.
                FailHandshake(connectionId, "Handshake was canceled.");
                return false;
            }
        }
        catch (Exception error)
        {
            FailHandshake(connectionId, Detail("An unexpected error occurred during connection handshake.", error));
            return false;
        }
        if (!string.Equals(request.Protocol, protocol.Name, StringComparison.Ordinal))
        {
            FailHandshake(connectionId, $"The protocol '{request.Protocol}' is not supported.");
            return false;
        }
        // Accepts 1 and 2 — IsVersionSupported is v <= 2. Version 1 is what a default client sends:
        // it downgrades whenever connection.features.reconnect is falsy, which it is unless
        // negotiate opted into stateful reconnect (HubConnection.ts:245-250). Accepting both while
        // never emitting useStatefulReconnect is the safe combination with the feature skipped.
        if (!protocol.IsVersionSupported(request.Version))
        {
            FailHandshake(connectionId,
                $"The server does not support version {request.Version} of the '{request.Protocol}' protocol.");
            return false;
        }
        state.Handshaken = true;
        transport.Attach(connectionId, state.Serialize());
        transport.SendRaw(connectionId, HandshakeSuccess);
        return true;
    }

    /// <summary>The two callbacks that follow a written handshake response, in upstream's order.</summary>
    private async Task AfterHandshake (string connectionId, ConnectionState state)
    {
        var connection = Rebuild(connectionId, state);
        await lifetime.OnConnectedAsync(connection).ConfigureAwait(false);
        var hub = Bind(connection);
        try { await hub.OnConnectedAsync().ConfigureAwait(false); }
        finally { hub.Dispose(); }
    }

    private void FailHandshake (string connectionId, string error)
    {
        var buffer = new ArrayBufferWriter<byte>();
        HandshakeProtocol.WriteResponseMessage(new HandshakeResponseMessage(error), buffer);
        transport.SendRaw(connectionId, System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan));
        transport.Close(connectionId, 1002, "handshake failed");
    }

    /// <summary>
    /// Upstream's <c>DispatchMessageAsync</c> switch (<c>DefaultHubDispatcher.cs:199-280</c>), arm
    /// for arm. The arms whose feature is skipped answer the client rather than going quiet:
    /// rule is that unsupported features fail loudly at compile time or at the protocol,
    /// never silently.
    /// </summary>
    private async Task DispatchAsync (HubConnectionContext connection, HubMessage message)
    {
        switch (message)
        {
            case InvocationBindingFailureMessage failure:
                await Complete(connection, failure.InvocationId, Detail(
                    $"Failed to invoke '{failure.Target}' due to an error on the server.",
                    failure.BindingFailure.SourceException)).ConfigureAwait(false);
                return;
            case InvocationMessage invocation:
                await InvokeAsync(connection, invocation).ConfigureAwait(false);
                return;
            case StreamInvocationMessage stream:
                // Server→client streaming is its own package; the
                // seam is the binder's GetStreamItemType, which stays wired.
                await Complete(connection, stream.InvocationId,
                    $"Streaming hub methods are not supported by this host; '{stream.Target}' cannot be streamed.").ConfigureAwait(false);
                return;
            case CancelInvocationMessage:
                // Nothing streams, so there is never an active cancellation source. Upstream logs
                // the same case as "unexpected cancel with id" and continues.
                return;
            case PingMessage:
                // Normally absorbed by workerd's auto-response without waking the actor at all
                // (legacy-hibernation-manager.c++:317-376); one that reaches here is still a
                // liveness signal, and upstream's response — StartClientTimeout — is a heartbeat
                // this host does not have. The sweep reads the auto-response timestamp instead.
                return;
            case StreamItemMessage:
                // Client→server upload streaming is SKIPped: HubMethodDescriptor
                // .ValidateParameterStreamType proves it cannot be made total under AOT.
                return;
            case CompletionMessage completion:
                // Client results are DEFERRED, so TryGetReturnType is the base implementation and
                // returns false — upstream's "unexpected completion" path.
                _ = completion;
                return;
            case AckMessage or SequenceMessage:
                // Stateful reconnect is SKIPped; a client that never negotiated it never sends these.
                return;
            case CloseMessage:
                connection.Abort();
                return;
            default:
                throw new NotSupportedException($"Received unsupported message: {message.GetType().Name}");
        }
    }

    private async Task InvokeAsync (HubConnectionContext connection, InvocationMessage invocation)
    {
        var slot = dispatcher.Slot(invocation.Target);
        if (slot < 0)
        {
            await Complete(connection, invocation.InvocationId,
                $"Unknown hub method '{invocation.Target}'.").ConfigureAwait(false);
            return;
        }
        var hub = Bind(connection);
        try
        {
            var result = await dispatcher.Invoke(slot, hub, invocation.Arguments ?? []).ConfigureAwait(false);
            if (invocation.InvocationId is not null)
                connection.Write(CompletionMessage.WithResult(invocation.InvocationId, result));
        }
        catch (HubException error)
        {
            // The one exception type whose message is a contract: upstream forwards it verbatim
            // regardless of EnableDetailedErrors.
            await Complete(connection, invocation.InvocationId, error.Message).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await Complete(connection, invocation.InvocationId, Detail(
                $"An unexpected error occurred invoking '{invocation.Target}' on the server.", error)).ConfigureAwait(false);
        }
        finally { hub.Dispose(); }
    }

    /// <summary>Handles a closed socket: hub callback, lifetime manager, then forget the queue.</summary>
    public async Task DisconnectAsync (string connectionId, string? reason)
    {
        try
        {
            if (!lifetime.TryGetConnection(connectionId, out var connection)) return;
            var hub = Bind(connection);
            try { await hub.OnDisconnectedAsync(reason is null ? null : new HubException(reason)).ConfigureAwait(false); }
            finally { hub.Dispose(); }
            await lifetime.OnDisconnectedAsync(connection).ConfigureAwait(false);
        }
        finally { queue.Forget(connectionId); }
    }

    /// <summary>
    /// The Durable Object alarm sweep that replaces upstream's <c>CheckClientTimeout</c>
    /// (<c>HubConnectionContext.cs:815-836</c>): close a socket that never handshook inside
    /// <see cref="HubOptions.HandshakeTimeout"/>, or a handshaken one whose last auto-response ping
    /// is older than <see cref="HubOptions.ClientTimeoutInterval"/>.
    /// </summary>
    /// <remarks>
    /// Read-only plus <c>close</c>, deliberately, rather than routed through the per-connection
    /// queues. An alarm is a third concurrent entrant — it runs to completion inside a message
    /// handler's await window (measured, <c>src/js/test/do-interleave</c> scenario 2) — so it must
    /// obey the same discipline as the lifetime manager. It does, by writing nothing: <c>close</c>
    /// is idempotent, so a socket the sweep and a handler both decide to close is closed once.
    /// </remarks>
    public Task<int> SweepAsync ()
    {
        var closed = 0;
        var timestamp = now();
        foreach (var socket in transport.Sockets())
        {
            if (!socket.Open) continue;
            var state = ConnectionState.Deserialize(socket.Attachment);
            var deadline = state.Handshaken
                ? Math.Max(socket.LastSeen, state.AcceptedAt) + options.ClientTimeoutInterval.TotalMilliseconds
                : state.AcceptedAt + options.HandshakeTimeout.TotalMilliseconds;
            if (timestamp <= deadline) continue;
            transport.Close(socket.ConnectionId, 1001, state.Handshaken ? "client timeout" : "handshake timeout");
            closed++;
        }
        return Task.FromResult(closed);
    }

    /// <summary>
    /// Sends <c>{"type":7}</c> before closing, which is what upstream's <c>SendCloseAsync</c> does
    /// (<c>HubConnectionHandler.cs:310-330</c>). <paramref name="allowReconnect"/> routes the client
    /// to <c>connection.stop()</c> — retried by its automatic-reconnect policy — instead of a
    /// permanent teardown (<c>HubConnection.ts:691-708</c>).
    /// </summary>
    public Task CloseAsync (string connectionId, string? error = null, bool allowReconnect = false)
    {
        transport.SendRaw(connectionId, protocol.Frame(new CloseMessage(error, allowReconnect)));
        transport.Close(connectionId, 1000, error ?? "");
        return Task.CompletedTask;
    }

    private Task Complete (HubConnectionContext connection, string? invocationId, string error)
    {
        // A non-blocking invocation (no id) gets no completion, exactly as upstream: the client is
        // not waiting for one and inventing an id would fail its lookup.
        if (invocationId is not null)
            connection.Write(CompletionMessage.WithError(invocationId, error));
        return Task.CompletedTask;
    }

    /// <summary>Builds the hub instance for one dispatch and wires its three properties.</summary>
    private THub Bind (HubConnectionContext connection)
    {
        var hub = dispatcher.Create();
        hub.Clients = new HubClients<THub>(lifetime, connection.ConnectionId);
        hub.Context = new WorkerHubCallerContext(connection);
        hub.Groups = new GroupManager<THub>(lifetime);
        return hub;
    }

    private HubConnectionContext Rebuild (string connectionId, ConnectionState state) =>
        new(connectionId, transport, protocol,
            state.UserIdentifier is null ? null : Principal(state.UserIdentifier), state.UserIdentifier);

    /// <summary>
    /// Rebuilds the minimum principal a hub method can read after a wake. Full claims are not
    /// reconstructed: ASP.NET authorization is out of scope and storing a whole
    /// principal in a 16 KiB attachment would be a silent truncation waiting to happen.
    /// </summary>
    private static ClaimsPrincipal Principal (string userIdentifier) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userIdentifier)], "Bootsharp.Cloudflare.SignalR"));

    private string Detail (string message, Exception? error) =>
        options.EnableDetailedErrors && error is not null ? $"{message} {error.GetType().Name}: {error.Message}" : message;

    /// <summary>
    /// The success handshake response, taken from the shipped assembly rather than restated:
    /// <c>GetSuccessfulHandshake</c> ignores its argument and returns a <c>static readonly</c>
    /// computed once (<c>HandshakeProtocol.cs:28-49</c>), so the entire server-side handshake
    /// response is the 3-byte constant <c>7B 7D 1E</c>. Spelling that as a literal here would put a
    /// raw control character in source and a second copy of a constant on the wire path.
    /// </summary>
    private string HandshakeSuccess =>
        System.Text.Encoding.UTF8.GetString(HandshakeProtocol.GetSuccessfulHandshake(protocol));

    /// <summary>
    /// The type feed for the reused parser. Upstream's <c>ProtocolHelper.TryGetReturnType</c> wraps
    /// <see cref="GetReturnType"/> in a try/catch and reads a <b>throw</b> as "unknown"
    /// (<c>common/Shared/TryGetReturnType.cs:10-23</c>) — a binder that returns null instead makes
    /// the parser dereference it. Throwing here is the contract, not a defect.
    /// </summary>
    private sealed class InvocationBinder (HubDispatcher<THub> dispatcher) : IInvocationBinder
    {
        private static readonly Type[] none = [];

        public Type GetReturnType (string invocationId) =>
            throw new InvalidOperationException($"No invocation with id '{invocationId}' is in progress.");

        public IReadOnlyList<Type> GetParameterTypes (string methodName)
        {
            var slot = dispatcher.Slot(methodName);
            // An unknown target is answered by the dispatch switch with a completion error, so the
            // parser is given an empty argument list rather than an exception that would abort the
            // whole frame and lose the invocation id with it.
            return slot < 0 ? none : dispatcher.ParameterTypes(slot);
        }

        public Type GetStreamItemType (string streamId) =>
            throw new InvalidOperationException($"No stream with id '{streamId}' is in progress.");
    }
}
