#!/usr/bin/env bash
# Tests for next-version.sh. Run it directly: .github/scripts/next-version.test.sh
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$script_dir/next-version.sh"

pass=0
fail=0

# Helpers available to each case's setup, which runs inside a throwaway repo.
commit() {
    if [[ $# -gt 1 ]]; then
        git commit -q --allow-empty -m "$1" -m "$2"
    else
        git commit -q --allow-empty -m "$1"
    fi
}

# check <name> <expected> <setup>
# <expected> is the script's three output lines joined by spaces.
check() {
    local name="$1" expected="$2" setup="$3"
    local dir actual
    dir="$(mktemp -d)"

    if ! (
        cd "$dir"
        git init -q -b main
        git config user.email test@example.com
        git config user.name "Test"
        eval "$setup"
    ) >/dev/null 2>&1; then
        printf '  FAIL %s\n       setup failed\n' "$name"
        fail=$((fail + 1))
        rm -rf "$dir"
        return
    fi

    actual="$(cd "$dir" && bash "$script" 2>/dev/null | tr '\n' ' ' | sed 's/ *$//')"
    if [[ "$actual" == "$expected" ]]; then
        printf '  ok   %s\n' "$name"
        pass=$((pass + 1))
    else
        printf '  FAIL %s\n       expected: %s\n       actual:   %s\n' "$name" "$expected" "$actual"
        fail=$((fail + 1))
    fi
    rm -rf "$dir"
}

echo "next-version.sh"

# --- no tags yet: the whole history is the range, counting up from 0.0.0 ---

check "first release from feat commits" \
    "version=0.1.0 bump=minor previous=0.0.0" \
    'commit "feat: initial implementation"'

check "first release from fix commits only" \
    "version=0.0.1 bump=patch previous=0.0.0" \
    'commit "fix: correct the port walk"'

check "no release when history has nothing releasable" \
    "version=0.0.0 bump=none previous=0.0.0" \
    'commit "docs: write the readme"'

# --- bumping from an existing tag ---

check "fix bumps patch" \
    "version=1.2.4 bump=patch previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "fix: stop double publishing"'

check "perf bumps patch" \
    "version=1.2.4 bump=patch previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "perf: cache the scan"'

check "feat bumps minor" \
    "version=1.3.0 bump=minor previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "feat: add a dashboard"'

check "feat outranks fix regardless of order" \
    "version=1.3.0 bump=minor previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "feat: add a dashboard"; commit "fix: typo"'

check "fix after feat does not downgrade the bump" \
    "version=1.3.0 bump=minor previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "fix: typo"; commit "feat: add a dashboard"'

check "the decisive commit is found even when it is the oldest in the range" \
    "version=2.0.0 bump=major previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "feat!: drop old config"; commit "fix: typo"; commit "docs: note it"'

check "a breaking footer is found on an older commit in the range" \
    "version=2.0.0 bump=major previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "feat: rework" "BREAKING CHANGE: config moved"; commit "fix: typo"'

check "docs and chore alone produce no release" \
    "version=1.2.3 bump=none previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "docs: clarify setup"; commit "chore: bump deps"'

check "non-conventional commits are ignored" \
    "version=1.2.3 bump=none previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "Add some stuff"'

check "commits before the tag are not counted" \
    "version=1.2.3 bump=none previous=1.2.3" \
    'commit "feat: counted into the tag"; git tag v1.2.3'

# --- scopes ---

check "scoped feat bumps minor" \
    "version=1.3.0 bump=minor previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "feat(cli): add --no-browser"'

check "scoped fix bumps patch" \
    "version=1.2.4 bump=patch previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "fix(web): guard the port picker"'

# --- breaking changes at 1.x and above ---

check "bang bumps major" \
    "version=2.0.0 bump=major previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "feat!: drop the old config format"'

check "scoped bang bumps major" \
    "version=2.0.0 bump=major previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "fix(api)!: rename the publish route"'

check "BREAKING CHANGE footer bumps major" \
    "version=2.0.0 bump=major previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "feat: rework config" "BREAKING CHANGE: config.json moved"'

check "BREAKING-CHANGE footer bumps major" \
    "version=2.0.0 bump=major previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "feat: rework config" "BREAKING-CHANGE: config.json moved"'

check "breaking mentioned mid-sentence is not a footer" \
    "version=1.2.4 bump=patch previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "fix: avoid BREAKING CHANGE: in prose" "This is not a real footer."'

# --- breaking changes below 1.0 bump the minor instead ---

check "pre-1.0 bang bumps minor, not major" \
    "version=0.2.0 bump=minor previous=0.1.0" \
    'commit "feat: base"; git tag v0.1.0; commit "feat!: drop the old config format"'

check "pre-1.0 BREAKING CHANGE footer bumps minor" \
    "version=0.2.0 bump=minor previous=0.1.0" \
    'commit "feat: base"; git tag v0.1.0; commit "feat: rework" "BREAKING CHANGE: config.json moved"'

check "a hand-placed v1.0.0 tag restores normal major bumps" \
    "version=2.0.0 bump=major previous=1.0.0" \
    'commit "feat: base"; git tag v1.0.0; commit "feat!: drop the old config format"'

# --- tag selection ---

check "the nearest version tag wins" \
    "version=1.3.0 bump=minor previous=1.2.3" \
    'commit "feat: base"; git tag v1.0.0; commit "feat: more"; git tag v1.2.3; commit "feat: newest"'

check "non-version tags are ignored" \
    "version=1.3.0 bump=minor previous=1.2.3" \
    'commit "feat: base"; git tag v1.2.3; commit "feat: more"; git tag nightly'

# --- malformed tags fail loudly rather than guessing ---

check "a prerelease tag is rejected" \
    "" \
    'commit "feat: base"; git tag v1.2.3-beta.1; commit "feat: more"'

printf '\n%d passed, %d failed\n' "$pass" "$fail"
[[ "$fail" -eq 0 ]]
