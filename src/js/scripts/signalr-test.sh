#!/usr/bin/env bash
# PACKAGE SR — the SignalR end-to-end lane (ADR-0010). Same conventions as interleave-test.sh:
# publish the way a Cloudflare Worker actually ships (BsLlvm=true, one native .wasm), then drive it
# under real workerd with the stock @microsoft/signalr npm client.
#
# It proves the claims no unit test can, because they are claims about somebody else's client and
# about workerd's hibernation:
#   1. negotiate answers with the four fields the client reads, and never useStatefulReconnect
#   2. the handshake completes and an invocation with arguments returns its result
#   3. a HubException's message reaches the client verbatim
#   4. Clients.All reaches two connections of the same Durable Object; a group reaches only members
#   5. the connection survives a hibernation wake (14 s idle; workerd local evicts at 10 s)
#
# Prerequisites, in order: src/cs/.scripts/llvm.sh (the ILCompiler-LLVM packs) and
# src/cs/.scripts/pack.sh (the Bootsharp packages the harness restores).
#
# Pass --publish-only to stop after the publish, for iterating on the worker by hand.
set -e

cd "$(dirname "$0")/../test/signalr"

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
dotnet publish backend/SignalR.Harness.csproj -c Release /p:BsLlvm=true "${RESOLVER[@]}"

[ "$1" = "--publish-only" ] && exit 0

node run.mjs
