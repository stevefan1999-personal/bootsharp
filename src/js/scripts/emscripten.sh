#!/usr/bin/env bash
# Sourced by every NativeAOT-LLVM lane script. Fills the RESOLVER array with the workload-resolver
# flag under EXACTLY the condition test/emscripten.props disables the resolver under: the Emscripten
# SDK pack directory exists, so the packs can be imported by path and no wasm-tools workload is
# needed. The lane project gets the property from the props file; the flag exists for the referenced
# library projects, which sit outside that file's directory scope.
#
# The test is on the pack directory, never on a version inside it — the packs are versioned with the
# runtime, and pinning a version here silently drops the flag the moment the host patches (it went
# 10.0.9 -> 10.0.11 mid-session and every lane started failing on the broken workload set instead).

EMSCRIPTEN_SDK_PACK=/usr/share/dotnet/packs/Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.linux-x64
RESOLVER=()
[ -d "$EMSCRIPTEN_SDK_PACK" ] && RESOLVER=(/p:MSBuildEnableWorkloadResolver=false)
