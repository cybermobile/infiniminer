#!/bin/sh
# Adds Infiniminer to your desktop's application menu, pointing at this folder.
# Run it again if you move the folder; delete ~/.local/share/applications/infiniminer.desktop to remove it.
set -e
dir=$(cd "$(dirname "$0")" && pwd)
target="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$target"
sed "s|__INSTALL_DIR__|$dir|g" "$dir/infiniminer.desktop" > "$target/infiniminer.desktop"
echo "Added Infiniminer to the application menu ($target/infiniminer.desktop)."
