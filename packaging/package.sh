#!/usr/bin/env bash
# Builds the release packages for one platform into dist/:
#
#   packaging/package.sh <rid> [version]
#
#   win-x64   Infiniminer-<version>-win-x64.zip (portable) and Infiniminer-<version>-win-x64-setup.exe
#             (the installer needs makensis: `apt install nsis`)
#   osx-*     Infiniminer-<version>-<rid>.zip holding Infiniminer.app and the server. Run this on macOS:
#             the app has to be signed there, and unsigned arm64 code won't start on Apple Silicon.
#   linux-*   Infiniminer-<version>-<rid>.tar.gz, and for linux-x64 an Infiniminer-<version>-x86_64.AppImage
#             (appimagetool is downloaded, pinned and checksummed, if it isn't installed)
#
# Every platform also gets InfiniminerServerList-<version>-<rid>, the public server list service.
# Everything is self-contained: players don't need .NET installed.
set -euo pipefail

rid=${1:?usage: packaging/package.sh <rid> [version]}
root=$(cd "$(dirname "$0")/.." && pwd)
version=${2:-$(sed -n 's/.*Version = "v\([0-9.]*\)".*/\1/p' "$root/src/Infiniminer.Shared/GameTypes.cs")}
work=$root/out/$rid
dist=$root/dist
name=Infiniminer-$version-$rid
listName=InfiniminerServerList-$version-$rid
rm -rf "$work"
mkdir -p "$work" "$dist"

publish() { # project, output folder
    echo "Publishing $1 for $rid..."
    dotnet publish "$root/src/$1" -c Release -r "$rid" --self-contained -p:Version="$version" -p:DebugType=none \
        -o "$2" --nologo -v quiet
}

docs() { # folder
    cp "$root/README" "$1/README.txt"
    cp "$root/LICENSE" "$1/LICENSE.txt"
}

zip_folder() { # parent folder, folder name, zip file
    rm -f "$3"
    if command -v ditto > /dev/null; then
        ditto -c -k --keepParent "$1/$2" "$3"    # keeps the app bundle's signature and permissions intact
    else
        (cd "$1" && zip -qry "$3" "$2")
    fi
}

tar_folder() { # parent folder, folder name, archive
    tar -C "$1" -czf "$3" "$2"
}

# appimagetool, pinned to a release and checked, unless one is installed.
APPIMAGETOOL_VERSION=1.9.1
APPIMAGETOOL_SHA256=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
find_appimagetool() {
    if command -v appimagetool > /dev/null; then
        echo appimagetool
        return
    fi
    tool=$root/out/tools/appimagetool-$APPIMAGETOOL_VERSION
    if [ ! -x "$tool" ]; then
        mkdir -p "$root/out/tools"
        curl -fsSL -o "$tool.download" \
            "https://github.com/AppImage/appimagetool/releases/download/$APPIMAGETOOL_VERSION/appimagetool-x86_64.AppImage" || return 1
        echo "$APPIMAGETOOL_SHA256  $tool.download" | sha256sum -c --quiet - || { rm -f "$tool.download"; return 1; }
        mv "$tool.download" "$tool"
        chmod +x "$tool"
    fi
    echo "$tool"
}

build_appimage() { # published game folder
    tool=$(find_appimagetool) || { echo "Could not get appimagetool; skipping the AppImage." >&2; return 0; }
    appdir=$work/Infiniminer.AppDir
    rm -rf "$appdir"
    mkdir -p "$appdir/usr/lib"
    cp -a "$1" "$appdir/usr/lib/infiniminer"
    cp "$root/packaging/linux/AppRun" "$appdir/AppRun"
    cp "$root/packaging/linux/infiniminer.png" "$appdir/infiniminer.png"
    cp "$root/packaging/linux/infiniminer.png" "$appdir/.DirIcon"
    # The tarball's menu entry points at its folder; inside the AppImage it runs AppRun.
    sed -e 's|^Exec=.*|Exec=Infiniminer|' -e '/^Path=/d' -e 's|^Icon=.*|Icon=infiniminer|' \
        "$root/packaging/linux/infiniminer.desktop" > "$appdir/infiniminer.desktop"
    # appimagetool is itself an AppImage; extracting it avoids needing FUSE (CI runners, containers).
    APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64 VERSION=$version "$tool" --no-appstream "$appdir" \
        "$dist/Infiniminer-$version-x86_64.AppImage" > "$work/appimagetool.log" 2>&1 \
        || { cat "$work/appimagetool.log" >&2; return 1; }
}

case $rid in
    win-*)
        # The client and server share the runtime, so they ship in one folder as the original did.
        game=$work/Infiniminer
        publish Infiniminer.Client "$game"
        publish Infiniminer.Server "$game"
        docs "$game"
        cp "$root/code/InfiniminerClient/Game.ico" "$game/Game.ico"
        zip_folder "$work" Infiniminer "$dist/$name.zip"
        if command -v makensis > /dev/null; then
            makensis -V2 -DVERSION="$version" -DSRCDIR="$game" -DOUTFILE="$dist/$name-setup.exe" \
                "$root/packaging/windows/infiniminer.nsi"
        else
            echo "makensis not found; skipping the installer." >&2
        fi
        ;;

    osx-*)
        folder=$work/Infiniminer
        app=$folder/Infiniminer.app
        mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
        publish Infiniminer.Client "$app/Contents/MacOS"
        cp "$root/packaging/macos/Infiniminer.icns" "$app/Contents/Resources/"
        sed "s/__VERSION__/$version/g" "$root/packaging/macos/Info.plist" > "$app/Contents/Info.plist"
        publish Infiniminer.Server "$folder/Server"
        docs "$folder"
        if command -v codesign > /dev/null; then
            # Ad-hoc signatures: enough for the code to run, though Gatekeeper still asks on first launch.
            codesign --force --deep --sign - "$app"
            codesign --force --sign - "$folder/Server/InfiniminerServer"
        else
            echo "codesign not found (not on macOS?); the app is unsigned and won't start on Apple Silicon." >&2
        fi
        zip_folder "$work" Infiniminer "$dist/$name.zip"
        ;;

    linux-*)
        game=$work/Infiniminer
        publish Infiniminer.Client "$game"
        publish Infiniminer.Server "$game"
        docs "$game"
        cp "$root/packaging/linux/infiniminer.png" "$root/packaging/linux/infiniminer.desktop" \
            "$root/packaging/linux/install-desktop-entry.sh" "$game/"
        tar_folder "$work" Infiniminer "$dist/$name.tar.gz"
        if [ "$rid" = linux-x64 ]; then
            build_appimage "$game"
        fi
        ;;

    *)
        echo "Unknown runtime identifier: $rid" >&2
        exit 1
        ;;
esac

list=$work/InfiniminerServerList
publish Infiniminer.ServerList "$list"
docs "$list"
case $rid in
    linux-*) tar_folder "$work" InfiniminerServerList "$dist/$listName.tar.gz" ;;
    *) zip_folder "$work" InfiniminerServerList "$dist/$listName.zip" ;;
esac

echo "Packages for $rid:"
ls -l "$dist" | grep -E -- "-$version-($rid|x86_64)" || true
