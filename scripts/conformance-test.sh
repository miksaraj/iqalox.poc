#!/usr/bin/env bash
# Cross-implementation conformance test (docs/PLAN-0.1.md, Phase 9): builds
# compiler/ and vm/, then runs every langspec/examples/*.iqx fixture through
# both poc/ and the compiler/+vm/ pipeline, diffing their stdout
# byte-for-byte. These fixtures are language-level, not implementation-
# specific, so their output must match exactly across implementations --
# behavioral drift is either an intentional 0.1-poc limitation being fixed
# (expected, and should be noted rather than silently accepted here) or a
# real regression worth catching immediately.
#
# Distinct from scripts/phase7-run-smoke-test.sh, which only checks that
# compiler/+vm/ runs each fixture to completion (exit 0) -- this script is
# the actual regression safety net across the two otherwise-independent
# implementations, per docs/PLAN-0.1.md section 7.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_bytecode="$(mktemp -t iqalox-conformance-XXXXXX.iqbc)"
tmp_poc_out="$(mktemp -t iqalox-conformance-poc-XXXXXX.txt)"
tmp_vm_out="$(mktemp -t iqalox-conformance-vm-XXXXXX.txt)"
trap 'rm -f "$tmp_bytecode" "$tmp_poc_out" "$tmp_vm_out"' EXIT

echo "==> Building compiler/"
dotnet build "$repo_root/compiler/src/Iqaloxc.fsproj" >/dev/null

echo "==> Building vm/"
cmake -S "$repo_root/vm" -B "$repo_root/vm/build" -DCMAKE_BUILD_TYPE=Debug >/dev/null
cmake --build "$repo_root/vm/build" -j"$(nproc)" >/dev/null

failed=0
for source in "$repo_root"/langspec/examples/*.iqx; do
    name="$(basename "$source")"
    echo "==> Comparing $name"

    python3 "$repo_root/poc/src/iqalox.py" "$source" >"$tmp_poc_out"

    dotnet run --project "$repo_root/compiler/src/Iqaloxc.fsproj" -- "$source" "$tmp_bytecode" >/dev/null
    "$repo_root/vm/build/iqaloxvm" "$tmp_bytecode" >"$tmp_vm_out"

    if ! diff -u "$tmp_poc_out" "$tmp_vm_out"; then
        echo "MISMATCH: $name -- poc and compiler/+vm output differ (see diff above)"
        failed=1
    fi
done

if [ "$failed" -ne 0 ]; then
    echo "FAIL: one or more langspec/examples/*.iqx fixtures produced diverging output"
    exit 1
fi

echo "OK: every langspec/examples/*.iqx fixture produces identical output in poc/ and compiler/+vm/"
