// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/HttpContext.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Shared;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Encapsulates all HTTP-specific information about an individual HTTP request.
/// </summary>
#if !BSCF_WORKERS // excised: debugger views live in src/Shared/Debugger, which is not vendored, and a DebuggerTypeProxy roots the proxy type in the AOT graph for a debugger workerd does not host
[DebuggerDisplay("{DebuggerToString(),nq}")]
[DebuggerTypeProxy(typeof(HttpContextDebugView))]
#endif // !BSCF_WORKERS
public abstract class HttpContext
{
    /// <summary>
    /// Gets the collection of HTTP features provided by the server and middleware available on this request.
    /// </summary>
    public abstract IFeatureCollection Features { get; }

    /// <summary>
    /// Gets the <see cref="HttpRequest"/> object for this request.
    /// </summary>
    public abstract HttpRequest Request { get; }

    /// <summary>
    /// Gets the <see cref="HttpResponse"/> object for this request.
    /// </summary>
    public abstract HttpResponse Response { get; }

    /// <summary>
    /// Gets information about the underlying connection for this request.
    /// </summary>
    public abstract ConnectionInfo Connection { get; }

    /// <summary>
    /// Gets an object that manages the establishment of WebSocket connections for this request.
    /// </summary>
    public abstract WebSocketManager WebSockets { get; }

    /// <summary>
    /// Gets or sets the user for this request.
    /// </summary>
    public abstract ClaimsPrincipal User { get; set; }

    /// <summary>
    /// Gets or sets a key/value collection that can be used to share data within the scope of this request.
    /// </summary>
    public abstract IDictionary<object, object?> Items { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="IServiceProvider"/> that provides access to the request's service container.
    /// </summary>
    public abstract IServiceProvider RequestServices { get; set; }

    /// <summary>
    /// Notifies when the connection underlying this request is aborted and thus request operations should be
    /// cancelled.
    /// </summary>
    public abstract CancellationToken RequestAborted { get; set; }

    /// <summary>
    /// Gets or sets a unique identifier to represent this request in trace logs.
    /// </summary>
    public abstract string TraceIdentifier { get; set; }

    /// <summary>
    /// Gets or sets the object used to manage user session data for this request.
    /// </summary>
    public abstract ISession Session { get; set; }

    /// <summary>
    /// Aborts the connection underlying this request.
    /// </summary>
    public abstract void Abort();

#if !BSCF_WORKERS // excised: the proxy bodies excised above
    private string DebuggerToString()
    {
        return HttpContextDebugFormatter.ContextToString(this, reasonPhrase: null);
    }

    private sealed class HttpContextDebugView(HttpContext context)
    {
        private readonly HttpContext _context = context;

        // Hide server specific implementations, they combine IFeatureCollection and many feature interfaces.
        public HttpContextFeatureDebugView Features => new HttpContextFeatureDebugView(_context.Features);
        public HttpRequest Request => _context.Request;
        public HttpResponse Response => _context.Response;
        public Endpoint? Endpoint => _context.GetEndpoint();
        public ConnectionInfo Connection => _context.Connection;
        public WebSocketManager WebSockets => _context.WebSockets;
        public ClaimsPrincipal User => _context.User;
        public IDictionary<object, object?> Items => _context.Items;
        public CancellationToken RequestAborted => _context.RequestAborted;
        public IServiceProvider RequestServices => _context.RequestServices;
        public string TraceIdentifier => _context.TraceIdentifier;
        // The normal session property throws if accessed before/without the session middleware.
        public ISession? Session => _context.Features.Get<ISessionFeature>()?.Session;
    }

    [DebuggerDisplay("Count = {Items.Length}")]
    private sealed class HttpContextFeatureDebugView(IFeatureCollection features)
    {
        private readonly IFeatureCollection _features = features;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public DictionaryItemDebugView<Type, object>[] Items => _features.Select(pair => new DictionaryItemDebugView<Type, object>(pair)).ToArray();
    }
#endif // !BSCF_WORKERS
}
