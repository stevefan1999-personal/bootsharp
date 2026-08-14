// API-identical reimplementations of ClientProxyExtensions and ISingleClientProxyExtensions
// (MIT,.NET Foundation). Upstream: src/SignalR/server/Core/src/ClientProxyExtensions.cs and
// ISingleClientProxyExtensions.cs. Signatures copied, bodies new.

namespace Microsoft.AspNetCore.SignalR;

/// <summary>
/// Extension methods for <see cref="IClientProxy"/>.
/// </summary>
/// <remarks>
/// These are what a hub body actually writes — <c>Clients.All.SendAsync("receive", user, message)</c>
/// — while the interface itself declares only <c>SendCoreAsync</c>. That split is upstream's and is
/// load-bearing rather than stylistic: an <c>object?[]</c> parameter would let a single
/// <c>string[]</c> argument bind to the params overload and arrive as N arguments instead of one.
/// The arity ladder is why <c>SendAsync</c> is not simply <c>params</c>.
/// </remarks>
public static class ClientProxyExtensions
{
    /// <summary>Invokes a method on the connection(s) represented by the proxy.</summary>
    public static Task SendAsync (this IClientProxy proxy, string method,
        CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [], cancellationToken);

    /// <inheritdoc cref="SendAsync(IClientProxy,string,CancellationToken)"/>
    public static Task SendAsync (this IClientProxy proxy, string method, object? arg1,
        CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [arg1], cancellationToken);

    /// <inheritdoc cref="SendAsync(IClientProxy,string,CancellationToken)"/>
    public static Task SendAsync (this IClientProxy proxy, string method, object? arg1, object? arg2,
        CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [arg1, arg2], cancellationToken);

    /// <inheritdoc cref="SendAsync(IClientProxy,string,CancellationToken)"/>
    public static Task SendAsync (this IClientProxy proxy, string method, object? arg1, object? arg2,
        object? arg3, CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [arg1, arg2, arg3], cancellationToken);

    /// <inheritdoc cref="SendAsync(IClientProxy,string,CancellationToken)"/>
    public static Task SendAsync (this IClientProxy proxy, string method, object? arg1, object? arg2,
        object? arg3, object? arg4, CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [arg1, arg2, arg3, arg4], cancellationToken);

    /// <inheritdoc cref="SendAsync(IClientProxy,string,CancellationToken)"/>
    public static Task SendAsync (this IClientProxy proxy, string method, object? arg1, object? arg2,
        object? arg3, object? arg4, object? arg5, CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [arg1, arg2, arg3, arg4, arg5], cancellationToken);

    /// <inheritdoc cref="SendAsync(IClientProxy,string,CancellationToken)"/>
    public static Task SendAsync (this IClientProxy proxy, string method, object? arg1, object? arg2,
        object? arg3, object? arg4, object? arg5, object? arg6,
        CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [arg1, arg2, arg3, arg4, arg5, arg6], cancellationToken);

    /// <inheritdoc cref="SendAsync(IClientProxy,string,CancellationToken)"/>
    public static Task SendAsync (this IClientProxy proxy, string method, object? arg1, object? arg2,
        object? arg3, object? arg4, object? arg5, object? arg6, object? arg7,
        CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [arg1, arg2, arg3, arg4, arg5, arg6, arg7], cancellationToken);

    /// <inheritdoc cref="SendAsync(IClientProxy,string,CancellationToken)"/>
    public static Task SendAsync (this IClientProxy proxy, string method, object? arg1, object? arg2,
        object? arg3, object? arg4, object? arg5, object? arg6, object? arg7, object? arg8,
        CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8], cancellationToken);

    /// <inheritdoc cref="SendAsync(IClientProxy,string,CancellationToken)"/>
    public static Task SendAsync (this IClientProxy proxy, string method, object? arg1, object? arg2,
        object? arg3, object? arg4, object? arg5, object? arg6, object? arg7, object? arg8, object? arg9,
        CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9], cancellationToken);

    /// <inheritdoc cref="SendAsync(IClientProxy,string,CancellationToken)"/>
    public static Task SendAsync (this IClientProxy proxy, string method, object? arg1, object? arg2,
        object? arg3, object? arg4, object? arg5, object? arg6, object? arg7, object? arg8, object? arg9,
        object? arg10, CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, [arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10], cancellationToken);
}

/// <summary>
/// Extension methods for <see cref="ISingleClientProxy"/>.
/// </summary>
/// <remarks>
/// Client results are DEFERRED with the seam kept. The ladder ships so that hub code
/// written against ASP.NET Core still compiles here and fails at the call with a named
/// <c>NotImplementedException</c> — which is rule: an unsupported feature fails loudly,
/// never silently. Wiring it later is a change to <c>InvokeCoreAsync</c> alone.
/// </remarks>
public static class ISingleClientProxyExtensions
{
    /// <summary>Invokes a method on the connection and waits for its result.</summary>
    public static Task<T> InvokeAsync<T> (this ISingleClientProxy proxy, string method,
        CancellationToken cancellationToken = default) =>
        proxy.InvokeCoreAsync<T>(method, [], cancellationToken);

    /// <inheritdoc cref="InvokeAsync{T}(ISingleClientProxy,string,CancellationToken)"/>
    public static Task<T> InvokeAsync<T> (this ISingleClientProxy proxy, string method, object? arg1,
        CancellationToken cancellationToken = default) =>
        proxy.InvokeCoreAsync<T>(method, [arg1], cancellationToken);

    /// <inheritdoc cref="InvokeAsync{T}(ISingleClientProxy,string,CancellationToken)"/>
    public static Task<T> InvokeAsync<T> (this ISingleClientProxy proxy, string method, object? arg1,
        object? arg2, CancellationToken cancellationToken = default) =>
        proxy.InvokeCoreAsync<T>(method, [arg1, arg2], cancellationToken);

    /// <inheritdoc cref="InvokeAsync{T}(ISingleClientProxy,string,CancellationToken)"/>
    public static Task<T> InvokeAsync<T> (this ISingleClientProxy proxy, string method, object? arg1,
        object? arg2, object? arg3, CancellationToken cancellationToken = default) =>
        proxy.InvokeCoreAsync<T>(method, [arg1, arg2, arg3], cancellationToken);

    /// <inheritdoc cref="InvokeAsync{T}(ISingleClientProxy,string,CancellationToken)"/>
    public static Task<T> InvokeAsync<T> (this ISingleClientProxy proxy, string method, object? arg1,
        object? arg2, object? arg3, object? arg4, CancellationToken cancellationToken = default) =>
        proxy.InvokeCoreAsync<T>(method, [arg1, arg2, arg3, arg4], cancellationToken);

    /// <inheritdoc cref="InvokeAsync{T}(ISingleClientProxy,string,CancellationToken)"/>
    public static Task<T> InvokeAsync<T> (this ISingleClientProxy proxy, string method, object? arg1,
        object? arg2, object? arg3, object? arg4, object? arg5,
        CancellationToken cancellationToken = default) =>
        proxy.InvokeCoreAsync<T>(method, [arg1, arg2, arg3, arg4, arg5], cancellationToken);
}
