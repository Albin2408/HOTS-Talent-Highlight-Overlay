#!/usr/bin/env bash
# Refresh the bundled builds after a game patch (the app warns you when it's time).
#
#   tools/refresh-builds.sh                # Icy Veins builds only
#   tools/refresh-builds.sh --win-rates    # also attach HeroesProfile win rates
#                                          #   (needs your token in HEROESPROFILE_API_TOKEN)
#
# Step 1 politely downloads every hero's Icy Veins page (stops at the first block, never retries);
# step 2 parses them into src/HotsOverlay.App/SeedBuilds and prints which heroes' builds changed.
# Afterwards rebuild the exe:  dotnet publish src/HotsOverlay.App -p:PublishProfile=win-x64
set -u

root="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

"$root/tools/fetch-pages.sh" "$work/pages" || { echo "Download stopped; the existing snapshot was left untouched." >&2; exit 1; }

dotnet run --project "$root/tools/SeedBuilder" -- "$work/pages" "$root/src/HotsOverlay.App/SeedBuilds" "$@"
status=$?

echo
echo "Snapshot updated. Rebuild the exe to ship it:  dotnet publish src/HotsOverlay.App -p:PublishProfile=win-x64"
exit $status
