#!/usr/bin/env bash
# Run smoke test (docs/PLAN-0.1.md): builds compiler/ and vm/, then compiles
# *and runs* every cross-implementation conformance fixture in
# langspec/examples/ through the real end-to-end pipeline, checking each
# one runs to completion (exit 0). Originally written in Phase 7 (when
# vm/ first had a native stdlib worth producing real output with);
# updated in Phase 8 to expect every fixture to succeed now that classes
# actually execute too, rather than special-casing the two that used to
# hit the "not yet supported" boundary error.
#
# Not the same thing as Phase 9's planned conformance suite: this only
# checks each fixture *runs*, not that its output matches poc/
# byte-for-byte -- that diff is Phase 9's job to formalize in CI (though a
# hand-run diff during Phase 8 already found all six fixtures match).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_bytecode="$(mktemp -t iqalox-run-smoke-XXXXXX.iqbc)"
trap 'rm -f "$tmp_bytecode"' EXIT

echo "==> Building compiler/"
dotnet build "$repo_root/compiler/src/Iqaloxc.fsproj" >/dev/null

echo "==> Building vm/"
cmake -S "$repo_root/vm" -B "$repo_root/vm/build" -DCMAKE_BUILD_TYPE=Debug >/dev/null
cmake --build "$repo_root/vm/build" -j"$(nproc)" >/dev/null

for source in "$repo_root"/langspec/examples/*.iqx; do
    name="$(basename "$source")"
    echo "==> Compiling and running $name"
    dotnet run --project "$repo_root/compiler/src/Iqaloxc.fsproj" -- "$source" "$tmp_bytecode"
    "$repo_root/vm/build/iqaloxvm" "$tmp_bytecode" >/dev/null
done

echo "OK: every langspec/examples/*.iqx fixture ran to completion"
