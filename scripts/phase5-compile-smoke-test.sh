#!/usr/bin/env bash
# Phase 5 compile-only smoke test (docs/PLAN-0.1.md): builds compiler/ and
# runs iqaloxc over every cross-implementation conformance fixture in
# langspec/examples/, checking each compiles to bytecode format v1 without
# error. Supersedes phase1-roundtrip-smoke-test.sh -- vm/ still only reads
# format v0 (rebuilding it to read v1 and actually execute programs is
# Phase 6's job), so there's no VM to round-trip through yet; this only
# proves the frontend alone handles real, non-toy source.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_bytecode="$(mktemp -t iqalox-compile-smoke-XXXXXX.iqbc)"
trap 'rm -f "$tmp_bytecode"' EXIT

echo "==> Building compiler/"
dotnet build "$repo_root/compiler/src/Iqaloxc.fsproj" >/dev/null

for source in "$repo_root"/langspec/examples/*.iqx; do
    echo "==> Compiling $(basename "$source")"
    dotnet run --project "$repo_root/compiler/src/Iqaloxc.fsproj" -- "$source" "$tmp_bytecode"
done

echo "OK: every langspec/examples/*.iqx fixture compiled"
