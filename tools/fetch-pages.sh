#!/usr/bin/env bash
# One-time, polite download of every hero's Icy Veins build-guide page, for tools/SeedBuilder to parse.
#   usage: tools/fetch-pages.sh <output-dir>
#
# - identifies itself honestly, ~1 request per 1.5s, only paths robots.txt allows
# - STOPS at the first non-200 response or Cloudflare challenge page: it never retries or tries to
#   get around a block. If that happens, don't push on — just try again later.
set -u
out="${1:?usage: fetch-pages.sh <output-dir>}"
ua="HotsOverlay/0.1 (personal-use build snapshot)"
mkdir -p "$out"

fetch() {  # fetch <url> <file>; returns non-zero if blocked
  local code
  code=$(curl -s -A "$ua" -o "$2" -w '%{http_code}' "$1")
  if [ "$code" != "200" ] || grep -q 'Attention Required' "$2"; then
    echo "STOP: $1 returned HTTP $code (or a challenge page). Not retrying." >&2
    rm -f "$2"
    return 1
  fi
}

fetch "https://www.icy-veins.com/heroes/" "$out/_index.html" || exit 1
slugs=$(grep -o 'href="//www.icy-veins.com/heroes/[a-z0-9-]*-build-guide"' "$out/_index.html" \
        | sed 's/.*heroes\///; s/-build-guide"//' | sort -u)

n=0; total=$(echo "$slugs" | wc -l)
for slug in $slugs; do
  n=$((n+1))
  if [ -s "$out/$slug.html" ]; then echo "[$n/$total] $slug (already have it)"; continue; fi
  sleep 1.5
  fetch "https://www.icy-veins.com/heroes/$slug-build-guide" "$out/$slug.html" || exit 1
  echo "[$n/$total] $slug"
done
echo "done: $total heroes in $out"
