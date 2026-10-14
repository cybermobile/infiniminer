#!/bin/bash
# Builds Infiniminer-<version>-x86_64.flatpak in dist/ (needs flatpak and flatpak-builder):
#   packaging/flatpak/build.sh [version]
# Install it with: flatpak install --user dist/Infiniminer-<version>-x86_64.flatpak
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
version=${1:-$(sed -n 's/.*Version = "v\([0-9.]*\)".*/\1/p' "$root/src/Infiniminer.Shared/GameTypes.cs")}
here=$root/packaging/flatpak
work=$root/out/flatpak

# The game itself, as for the tarball (out/linux-x64/Infiniminer).
"$root/packaging/package.sh" linux-x64 "$version"

flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak-builder --user --install-deps-from=flathub --force-clean --repo="$work/repo" \
    "$work/build" "$here/io.github.cybermobile.Infiniminer.yml"
flatpak build-bundle "$work/repo" "$root/dist/Infiniminer-$version-x86_64.flatpak" io.github.cybermobile.Infiniminer
echo "Built dist/Infiniminer-$version-x86_64.flatpak"
