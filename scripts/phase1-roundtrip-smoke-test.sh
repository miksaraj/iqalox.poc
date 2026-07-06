#!/usr/bin/env bash
# Phase 1 round-trip proof (docs/PLAN-0.1.md): builds compiler/ and vm/,
# has iqaloxc emit its hardcoded hello-world chunk, feeds it to iqaloxvm,
# and checks the output matches. Not a substitute for either project's own
# test suite -- this only proves the two toolchains agree on the bytecode
# format end to end.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_bytecode="$(mktemp -t iqalox-roundtrip-XXXXXX.iqbc)"
trap 'rm -f "$tmp_bytecode"' EXIT

expected="Hello from the Iqalox bytecode VM!"

echo "==> Building vm/"
cmake -S "$repo_root/vm" -B "$repo_root/vm/build" -DCMAKE_BUILD_TYPE=Debug >/dev/null
cmake --build "$repo_root/vm/build" -j"$(nproc)" >/dev/null

echo "==> Building compiler/"
dotnet build "$repo_root/compiler/src/Iqaloxc.fsproj" >/dev/null

echo "==> Running iqaloxc to emit bytecode"
dotnet run --project "$repo_root/compiler/src/Iqaloxc.fsproj" -- "$tmp_bytecode"

echo "==> Running iqaloxvm on the emitted bytecode"
actual="$("$repo_root/vm/build/iqaloxvm" "$tmp_bytecode")"

if [[ "$actual" != "$expected" ]]; then
    echo "FAIL: expected '$expected', got '$actual'" >&2
    exit 1
fi

echo "OK: round trip produced '$actual'"
