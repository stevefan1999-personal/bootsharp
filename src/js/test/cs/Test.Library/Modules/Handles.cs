using System;
using Bootsharp;

namespace Test.Library;

/// <summary>
/// Handle whose instances outlive the invocation importing them.
/// </summary>
[JSHandle(Scope = HandleScope.Isolate)]
public interface IIsolateHandle
{
    string GetId ();
}

/// <summary>
/// Handle released when the invocation importing it ends, or earlier via dispose.
/// </summary>
[JSHandle("{ readonly kind: string }")]
public interface IScopedHandle : IDisposable
{
    string GetKind ();
}
