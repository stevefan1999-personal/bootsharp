# Third-party notices

`Bootsharp.Cloudflare.Generate` includes source code from the following third-party project.

## ASP.NET Core

- Project: [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore)
- Commit: `80de79ab881ab6b3899146db6c6fb8f8d339fd94`
- Copyright (c) .NET Foundation and Contributors
- License: MIT

The files under `MinimalApi/Vendored/` are copied verbatim from that repository — the Roslyn
utilities its Request Delegate Generator is built from
(`src/Http/Http.Extensions/gen/Microsoft.AspNetCore.Http.RequestDelegateGenerator.csproj:24-36`
lists them as shared sources):

| file | upstream | why it is here |
| --- | --- | --- |
| `ParsabilityHelper.cs` | `src/Shared/RoslynUtils/ParsabilityHelper.cs` | The `IParsable<T>` / `TryParse(string, IFormatProvider, out T)` / `TryParse(string, out T)` / enum / `Uri` ladder ADR-0008 §2 binds route, query and header parameters with. Only its parsability half is called. |
| `WellKnownTypes.cs`, `WellKnownTypeData.cs` | `src/Shared/RoslynUtils/` | Symbol lookup the helper above resolves `IFormatProvider` and `IParsable<T>` through. |
| `SymbolExtensions.cs` | `src/Shared/RoslynUtils/SymbolExtensions.cs` | `GetThisAndBaseTypes` for the `TryParse` search, and `GetDefaultValueString` for the default values the emitted delegate type has to reproduce. |
| `BoundedCacheWithFactory.cs` | `src/Shared/RoslynUtils/BoundedCacheWithFactory.cs` | The cache `ParsabilityHelper` keys on type symbols. |
| `CodeWriter.cs` | `src/Shared/RoslynUtils/CodeWriter.cs` | The indenting writer the RDG's emitters are written against. |

Every file carries its upstream path and commit in a header. This assembly is an analyzer: nothing
under `Vendored/` is shipped to an app's runtime, let alone to the wasm module.

Nothing else of the Request Delegate Generator is vendored — its parser and emitter are written
against `RequestDelegateFactory`'s options, metadata results and `MethodInfo` plumbing, none of
which exists here, and its interceptor predicate only fires for call sites resolving into an
assembly named `Microsoft.AspNetCore.Routing` (research/09 §10). What is copied instead is its
*emitted shape*, which its checked-in baselines document.

### MIT License

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
