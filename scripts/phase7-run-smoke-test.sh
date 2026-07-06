#!/usr/bin/env bash
# Phase 7 run smoke test (docs/PLAN-0.1.md): builds compiler/ and vm/, then
# compiles *and runs* every cross-implementation conformance fixture in
# langspec/examples/ through the real end-to-end pipeline. Supersedes
# Phase 5's compile-only script -- vm/ can actually execute a compiled
# program now (Phase 6) and, as of this phase, has a native stdlib to
# produce real output with (`print`/`concat`), so merely compiling is no
# longer the interesting question.
#
# Not the same thing as Phase 9's planned conformance suite: this only
# checks each fixture runs to the expected outcome (0 for a full program,
# 70 for one that hits the Phase 8 classes boundary), not that its output
# matches poc/ byte-for-byte -- that diff is Phase 9's job, once vm/ can
# run every fixture to completion.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_bytecode="$(mktemp -t iqalox-run-smoke-XXXXXX.iqbc)"
trap 'rm -f "$tmp_bytecode"' EXIT

echo "==> Building compiler/"
dotnet build "$repo_root/compiler/src/Iqaloxc.fsproj" >/dev/null

echo "==> Building vm/"
cmake -S "$repo_root/vm" -B "$repo_root/vm/build" -DCMAKE_BUILD_TYPE=Debug >/dev/null
cmake --build "$repo_root/vm/build" -j"$(nproc)" >/dev/null

# Every example still using classes hits Phase 8's "not yet supported"
# boundary error (exit 70) rather than running to completion -- expected
# for now, not a failure of this script.
classes_examples=("classes.iqx" "inheritance.iqx")

for source in "$repo_root"/langspec/examples/*.iqx; do
    name="$(basename "$source")"
    echo "==> Compiling and running $name"
    dotnet run --project "$repo_root/compiler/src/Iqaloxc.fsproj" -- "$source" "$tmp_bytecode"

    expected=0
    for classes_example in "${classes_examples[@]}"; do
        if [[ "$name" == "$classes_example" ]]; then
            expected=70
        fi
    done

    set +e
    "$repo_root/vm/build/iqaloxvm" "$tmp_bytecode" >/dev/null
    actual=$?
    set -e

    if [[ "$actual" != "$expected" ]]; then
        echo "FAIL: $name exited $actual, expected $expected" >&2
        exit 1
    fi
done

echo "OK: every langspec/examples/*.iqx fixture ran to its expected outcome"
