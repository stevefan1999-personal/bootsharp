# Third-party notices

`Bootsharp.Cloudflare.AspNetCore` includes source code from the following third-party project.

## ASP.NET Core

- Project: [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore)
- Commit: `80de79ab881ab6b3899146db6c6fb8f8d339fd94`
- Copyright (c) .NET Foundation and Contributors
- License: MIT

The files under `Vendored/` are copied from that repository, in some cases with the local changes
recorded in `Vendored/manifest.json` and listed in `Vendored/README.md`. Each file carries its
upstream path and the commit it was taken from in a header. `Microsoft.AspNetCore.App` publishes no
`browser-wasm` runtime pack, so these assemblies cannot be referenced from a Cloudflare Worker;
vendoring the MIT-licensed source is the same approach Microsoft takes for Blazor, in
`src/Components/Components/src/Microsoft.AspNetCore.Components.Routing.targets`.

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
