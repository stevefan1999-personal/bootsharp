#!/usr/bin/env bash
# PACKAGE H — the Durable Object event-interleaving harness (ADR-0010 §6, the milestone that gates
# every other piece of SignalR work). Same conventions as llvm-test.sh: publish the way a Cloudflare
# Worker actually ships (BsLlvm=true, one native .wasm), then drive it under real workerd.
#
# It answers three questions with instrumented C# handlers rather than with reasoning:
#   1. can two webSocketMessage events interleave while a hub-method-shaped handler awaits?
#   2. can a DO alarm fire mid-await?
#   3. what survives a hibernation wake mid-conversation?
#
# Prerequisites, in order: src/cs/.scripts/llvm.sh (the ILCompiler-LLVM packs) and
# src/cs/.scripts/pack.sh (the Bootsharp packages the harness restores).
#
# Pass --publish-only to stop after the publish, for iterating on the worker by hand.
# BS_INTERLEAVE_ORIGIN=https://… skips the publish and wrangler dev; the driver talks to that origin.
set -e

cd "$(dirname "$0")/../test/do-interleave"

if [ -n "${BS_INTERLEAVE_ORIGIN:-}" ]; then
  node run.mjs
  exit
fi

if [ ! -f ../../../cs/.llvm/microsoft.dotnet.ilcompiler.llvm/build/Microsoft.DotNet.ILCompiler.LLVM.targets ]; then
  echo "NativeAOT-LLVM artifacts are not downloaded. Run src/cs/.scripts/llvm.sh." >&2
  exit 1
fi

if ! ls ../../../cs/.nuget/Bootsharp.[0-9]*.nupkg >/dev/null 2>&1; then
  echo "Bootsharp is not packed. Run src/cs/.scripts/pack.sh." >&2
  exit 1
fi

# The referenced library projects sit outside this lane's Directory.Build.props scope, so the
# workload-resolver flag has to travel on the command line, under the same condition that file uses.
source ../../scripts/emscripten.sh

[ -d node_modules ] || npm install

rm -rf backend/bin backend/obj dist
dotnet publish backend/Interleave.Harness.csproj -c Release /p:BsLlvm=true "${RESOLVER[@]}"

[ "$1" = "--publish-only" ] && exit 0

node run.mjs
