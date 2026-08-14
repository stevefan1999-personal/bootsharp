#!/usr/bin/env bash
set -e

if [ ! -f .llvm/microsoft.dotnet.ilcompiler.llvm/build/Microsoft.DotNet.ILCompiler.LLVM.targets ]; then
  echo "NativeAOT-LLVM artifacts are not downloaded. Run ./.scripts/llvm.sh." >&2
  exit 1
fi

# Bootsharp packs ../../src/js/dist as its js/ payload. Without it the package still builds and
# still restores — it just publishes an app with no ES modules, which fails much later (or not at
# all, when a stale dist/ from a previous toolchain is still lying around). Fail here instead.
if [ ! -f ../js/dist/index.mjs ]; then
  echo "Bootsharp ES modules are not built. Run 'npm install && npm run build' in src/js." >&2
  exit 1
fi

mkdir -p .nuget
dotnet build Bootsharp.Generate -c Release
dotnet build Bootsharp.Cloudflare.Generate -c Release
dotnet pack Bootsharp.Common -o .nuget -c Release
dotnet pack Bootsharp.Inject -o .nuget -c Release
dotnet pack Bootsharp -o .nuget -c Release
dotnet pack Bootsharp.Cloudflare.Publish -o .nuget -c Release
dotnet pack Bootsharp.Cloudflare -o .nuget -c Release
dotnet pack Bootsharp.Cloudflare.AspNetCore -o .nuget -c Release
dotnet pack Bootsharp.Cloudflare.SignalR -o .nuget -c Release
# Analyzer-only, and the one package that redistributes a third-party assembly: the pinned Razor
# compiler ships beside the generator under analyzers/dotnet/cs, which is how the dependency resolves
# at build time and why nothing of Razor reaches a published worker. See ADR-0011 §3.
dotnet pack Bootsharp.Cloudflare.Razor -o .nuget -c Release
# Packed so the layer is buildable and testable, NOT because it is shippable yet: under
# NativeAOT-LLVM every reference-typed field of Microsoft's RenderTreeFrame reads back null, so
# StaticHtmlRenderer cannot write HTML inside workerd. See ADR-0011 §2 and the milestone-6 report.
dotnet pack Bootsharp.Cloudflare.Components -o .nuget -c Release
dotnet restore
