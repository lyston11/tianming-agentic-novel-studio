#!/bin/sh
# Verify the three Node packages: build, type-check, test, plus the boundary
# guards that keep tianming-novel-agent an adapter rather than a second domain.
#
# Why this script exists: the requirements were prose in
# .trellis/spec/backend/database-guidelines.md:234-240, executed by hand. Nothing
# ran them, so 11 tests and a type-check protected the only TypeScript vertical
# slice with no gate at all. This is the single entry point a CI job or a task
# checklist can call.
#
# Build order matters. The packages depend on each other by `file:`, which
# resolves against the upstream dist/, so upstream must be built first:
#   tianming-ai -> tianming-agent-core -> tianming-novel-agent
#
# Usage (from anywhere):
#   ./Scripts/verify-node-packages.sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd)

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  exit 1
}

command -v node >/dev/null 2>&1 || fail "node is not on PATH"
command -v npm  >/dev/null 2>&1 || fail "npm is not on PATH"

# Node >=22 is required by every package's engines field.
node_major=$(node -p 'process.versions.node.split(".")[0]')
[ "$node_major" -ge 22 ] || fail "Node >=22 required, found $(node -v)"

printf '== Node %s ==\n\n' "$(node -v)"

# Boundary guards run FIRST. They are pure source greps, independent of build
# state, and they encode decisions -- a violation is a design regression, not a
# style nit. Running them after the build would let a stale dist/ or a failing
# compile mask the finding that actually matters.
printf '== tianming-novel-agent boundary guards ==\n'
na="$repo_root/tianming-novel-agent"

# 1. Only @tianming/agent-core may be imported from the agent stack.
#    (.trellis/spec/backend/quality-guidelines.md:51)
if grep -rn '@mariozechner/pi-\|@earendil-works/pi-' "$na/src" 2>/dev/null; then
  fail "src/ imports a Pi package directly; only @tianming/agent-core is allowed"
fi

# 2. No infrastructure in the adapter: durable state belongs to C#.
#    (.trellis/spec/backend/database-guidelines.md:207-208)
if grep -rniE "\b(pg|postgres|npgsql|ioredis|redis|qdrant)\b" "$na/src" 2>/dev/null; then
  fail "src/ references a database/cache/vector client; durable state is C#-owned"
fi

# 3. The 8 NovelCommandError codes are a pinned cross-boundary vocabulary.
#    (.trellis/spec/backend/error-handling.md:91)
for code in not_found forbidden invalid_argument conflict invalid_transition model_error cancelled aborted; do
  grep -q "\"$code\"" "$na/src/contracts.ts" || fail "NovelCommandError code missing: $code"
done

printf '   Pi imports: none\n'
printf '   infrastructure clients: none\n'
printf '   NovelCommandError codes: 8/8 present\n\n'

# In dependency order: a `file:` dependency reads the upstream dist/.
for pkg in tianming-ai tianming-agent-core tianming-novel-agent; do
  dir="$repo_root/$pkg"
  [ -d "$dir" ] || fail "missing package directory: $dir"

  printf '== %s ==\n' "$pkg"
  [ -d "$dir/node_modules" ] || (cd "$dir" && npm install --silent)

  # Inspect the dist a PREVIOUS build left behind, before this run overwrites it.
  # Checking after the build can never fail: `build` cleans and re-emits, so dist/
  # is fresh by construction. The failure being caught is a committed or
  # locally-stale dist that a reader can mistake for current state — on
  # 2026-09-01 dist/src/store/in-memory-store.js survived its source's deletion
  # in 6564aaa9 and was read back as evidence the package still implemented a
  # Canon-merging store, which it did not.
  if [ -f "$dir/dist/src/index.js" ]; then
    newer=$(find "$dir/src" -name '*.ts' -newer "$dir/dist/src/index.js" 2>/dev/null | wc -l | tr -d ' ')
    orphans=0
    for emitted in $(find "$dir/dist/src" -name '*.js' 2>/dev/null); do
      rel=${emitted#"$dir/dist/src/"}
      [ -f "$dir/src/${rel%.js}.ts" ] || orphans=$((orphans + 1))
    done
    [ "$newer" = "0" ] || printf '   stale dist: %s source file(s) newer than dist/src/index.js\n' "$newer"
    [ "$orphans" = "0" ] || printf '   stale dist: %s emitted file(s) with no source\n' "$orphans"
    if [ "$newer" != "0" ] || [ "$orphans" != "0" ]; then
      fail "$pkg: dist/ is stale; a build did not clean. Run 'npm run build' and commit or delete dist/"
    fi
  fi

  (cd "$dir" && npm run --silent build)      || fail "$pkg: build"
  (cd "$dir" && npm run --silent type-check) || fail "$pkg: type-check"
  (cd "$dir" && npm test --silent)           || fail "$pkg: tests"

  printf '   dist fresh, build, type-check, tests OK\n\n'
done

printf 'All Node package checks passed.\n'
