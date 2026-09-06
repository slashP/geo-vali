#!/usr/bin/env bash
# Derives the next semantic version from the Conventional Commits made since the
# last v<major>.<minor>.<patch> tag, and writes it as GitHub Actions outputs:
#
#   version=0.2.0
#   bump=minor
#   previous=0.1.0
#
# bump is none when nothing releasable landed, which is the signal to skip the
# release. Below 1.0.0 a breaking change bumps the minor rather than declaring
# the tool stable; tag v1.0.0 by hand to opt into real major bumps.
set -euo pipefail

last_tag="$(git describe --tags --abbrev=0 --match='v[0-9]*.[0-9]*.[0-9]*' 2>/dev/null || true)"

if [[ -n "$last_tag" ]]; then
    if [[ ! "$last_tag" =~ ^v([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
        echo "next-version: last tag '$last_tag' is not a plain vX.Y.Z release tag." >&2
        exit 1
    fi
    major="${BASH_REMATCH[1]}"
    minor="${BASH_REMATCH[2]}"
    patch="${BASH_REMATCH[3]}"
    range="${last_tag}..HEAD"
else
    major=0 minor=0 patch=0
    range="HEAD"
fi

previous="${major}.${minor}.${patch}"

# NUL-delimited so a commit body's blank lines stay part of that commit.
bump=none
while IFS= read -r -d '' message; do
    # git separates records with a newline, so every message after the first
    # arrives with a leading blank line. Drop it before reading the subject.
    while [[ "$message" == $'\n'* ]]; do
        message="${message#$'\n'}"
    done

    subject="${message%%$'\n'*}"

    if [[ "$subject" =~ ^[a-zA-Z]+(\([^\)]*\))?!: ]] ||
        grep -qE '^BREAKING[ -]CHANGE:' <<<"$message"; then
        bump=major
        break
    elif [[ "$subject" =~ ^feat(\([^\)]*\))?: ]]; then
        bump=minor
    elif [[ "$subject" =~ ^(fix|perf)(\([^\)]*\))?: ]] && [[ "$bump" != minor ]]; then
        bump=patch
    fi
done < <(git log --format='%B%x00' "$range" --)

case "$bump" in
major)
    if [[ "$major" -eq 0 ]]; then
        echo "next-version: breaking change below 1.0.0 — bumping the minor instead." >&2
        minor=$((minor + 1))
        patch=0
        bump=minor
    else
        major=$((major + 1))
        minor=0
        patch=0
    fi
    ;;
minor)
    minor=$((minor + 1))
    patch=0
    ;;
patch)
    patch=$((patch + 1))
    ;;
esac

echo "version=${major}.${minor}.${patch}"
echo "bump=${bump}"
echo "previous=${previous}"
