#!/usr/bin/env bash
# The NativeAOT-LLVM end-to-end lane (ADR-0006 §4, research/06 implication 10). compile-test.sh
# publishes -c Debug, which is Mono; this publishes the same way a Cloudflare Worker ships —
# BsLlvm=true, one native .wasm — and runs the exercise worker under real workerd, asserting the
# ADR-0002 Tier-1 capabilities where they actually ship.
#
# Prerequisites, in order: src/cs/.scripts/llvm.sh (the ILCompiler-LLVM packs) and
# src/cs/.scripts/pack.sh (the Bootsharp packages the exercise project restores).
#
# Pass --publish-only to stop after the publish, for iterating on the worker by hand.
set -e

cd "$(dirname "$0")/../test/llvm"

if [ ! -f ../../../cs/.llvm/microsoft.dotnet.ilcompiler.llvm/build/Microsoft.DotNet.ILCompiler.LLVM.targets ]; then
  echo "NativeAOT-LLVM artifacts are not downloaded. Run src/cs/.scripts/llvm.sh." >&2
  exit 1
fi

if ! ls ../../../cs/.nuget/Bootsharp.[0-9]*.nupkg >/dev/null 2>&1; then
  echo "Bootsharp is not packed. Run src/cs/.scripts/pack.sh." >&2
  exit 1
fi

# The referenced library projects sit outside test/llvm, so Directory.Build.props does not reach
# them and the flag has to travel on the command line. It is applied under exactly the condition
# that file applies it under: only where Emscripten can be imported by path instead of resolved.
EMSDK=/usr/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.linux-x64/10.0.9/Sdk/Sdk.props
RESOLVER=()
[ -f "$EMSDK" ] && RESOLVER=(/p:MSBuildEnableWorkloadResolver=false)

[ -d node_modules ] || npm install

rm -rf backend/bin backend/obj dist
dotnet publish backend/Llvm.Exercise.csproj -c Release /p:BsLlvm=true "${RESOLVER[@]}"

[ "$1" = "--publish-only" ] && exit 0

# The emitted module is publish output; type-checking it is the cheap half of the gate.
npx wrangler types
npx tsc --noEmit -p tsconfig.json

node run.mjs
