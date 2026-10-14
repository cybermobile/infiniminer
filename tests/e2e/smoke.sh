#!/usr/bin/env bash
# End-to-end smoke test: runs a real dedicated server and the real game under a virtual display, plays with
# a scripted controller, and checks what happened from the game's trace output and the server log.
# Screenshots of each step are saved to $OUT (default out/e2e).
#
#   tests/e2e/smoke.sh [Debug|Release]
#
# Needs Xvfb, xdotool and ImageMagick (apt install xvfb xdotool imagemagick). Builds the solution first
# if the configuration hasn't been built.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
config=${1:-Debug}
out=${OUT:-$root/out/e2e}
port=5611
display=:${DISPLAY_NUMBER:-97}
work=$(mktemp -d)
pids=()

cleanup() {
    for pid in "${pids[@]}"; do kill "$pid" 2> /dev/null || true; done
    exec 3>&- 4>&- 2> /dev/null || true
    sleep 1
    rm -rf "$work"
}
trap cleanup EXIT

fail() {
    echo "FAILED: $1" >&2
    echo "--- game log ---" >&2; tail -40 "$work/client.log" >&2 || true
    echo "--- server log ---" >&2; tail -40 "$work/server.log" >&2 || true
    shot failure || true
    exit 1
}

# Waits until `file` has at least `count` lines matching `pattern`.
wait_for() { # file pattern [count] [seconds]
    local file=$1 pattern=$2 count=${3:-1} seconds=${4:-60}
    for ((i = 0; i < seconds * 10; i++)); do
        if [ "$(grep -c -- "$pattern" "$file" 2> /dev/null || true)" -ge "$count" ]; then
            return 0
        fi
        sleep 0.1
    done
    fail "waited ${seconds}s for $count x '$pattern' in $(basename "$file")"
}

step=0
shot() {
    step=$((step + 1))
    DISPLAY=$display import -window root "$out/$(printf %02d $step)_$1.png" 2> /dev/null || true
}

pad() { echo "$*" >&3; }
press() { pad "down $1"; sleep 0.15; pad "up $1"; sleep "${2:-0.4}"; }
game() { grep -c -- "$1" "$work/client.log" || true; }

client_bin=$root/src/Infiniminer.Client/bin/$config/net8.0
server_bin=$root/src/Infiniminer.Server/bin/$config/net8.0
if [ ! -f "$client_bin/Infiniminer.dll" ] || [ ! -f "$server_bin/InfiniminerServer.dll" ]; then
    dotnet build "$root/src/Infiniminer.sln" -c "$config" --nologo -v quiet
fi
mkdir -p "$out"
rm -f "$out"/*.png

# The server: a small map without lava (so the scripted player can't wander into it), on its own port.
cp -r "$server_bin" "$work/server"
cat > "$work/server/server.config.txt" << EOF
servername = Smoke Test
port = $port
includelava = false
homebases = false
EOF
mkfifo "$work/server.in"
(cd "$work/server" && exec dotnet InfiniminerServer.dll < "$work/server.in" > "$work/server.log" 2>&1) &
pids+=($!)
exec 4> "$work/server.in"
wait_for "$work/server.log" "SERVER READY"

# The game, on a virtual display, controlled by a scripted controller, joining with --connect.
Xvfb "$display" -screen 0 1024x768x24 > /dev/null 2>&1 &
pids+=($!)
sleep 1
cp -r "$client_bin" "$work/client"
mkfifo "$work/pad"
(cd "$work/client" && DISPLAY=$display INFINIMINER_TRACE=1 INFINIMINER_TEST_GAMEPAD="$work/pad" \
    exec dotnet Infiniminer.dll --connect "127.0.0.1:$port" > "$work/client.log" 2>&1) &
pids+=($!)
exec 3> "$work/pad"

echo "Joining with --connect..."
wait_for "$work/client.log" "TRACE JOINED"
wait_for "$work/client.log" "TRACE STATE TeamSelectionState"
window=$(DISPLAY=$display xdotool search --sync --name Infiniminer | head -1)
DISPLAY=$display xdotool windowfocus "$window"
sleep 0.5
shot team

echo "Choosing a team and class with the controller..."
press DPadUp
press A
wait_for "$work/client.log" "TRACE STATE ClassSelectionState"
shot class
press A
wait_for "$work/client.log" "TRACE STATE MainGameState"
wait_for "$work/server.log" "PLAYER_ALIVE"

echo "Playing: looking down and digging..."
sleep 1
pad "rstick 0 -0.9"; sleep 0.8; pad "rstick 0 0"
pad "rt 1"; sleep 3; pad "rt 0"; sleep 0.5
shot digging

echo "Sending a quick chat message (LT + D-pad)..."
pad "lt 1"; sleep 0.3
shot quick_chat
press DPadRight
pad "lt 0"
wait_for "$work/server.log" "FOUND GOLD!"

echo "Opening and closing the in-game menu..."
press Start 1
wait_for "$work/client.log" "TRACE STATE GameMenuState"
shot menu
press B 1
wait_for "$work/client.log" "TRACE STATE MainGameState" 2

echo "Restarting the server: the game should rejoin by itself..."
echo "restart" >&4
wait_for "$work/client.log" "TRACE REJOINING"
shot rejoining
wait_for "$work/client.log" "TRACE JOINED" 2
wait_for "$work/client.log" "TRACE STATE TeamSelectionState" 2
press DPadUp
press A
wait_for "$work/client.log" "TRACE STATE ClassSelectionState" 2
press A
wait_for "$work/client.log" "TRACE STATE MainGameState" 3
shot rejoined

echo "Leaving the server from the menu..."
press Start 1
wait_for "$work/client.log" "TRACE STATE GameMenuState" 2
press DPadUp            # wraps round to the last row, LEAVE SERVER
press A
wait_for "$work/client.log" "TRACE STATE ServerBrowserState" 3
wait_for "$work/server.log" "DISCONNECT: Player"
sleep 1
shot server_list

echo "Hosting a game from the server list..."
press DPadDown
press DPadUp            # the top row, HOST A GAME
press A 1
wait_for "$work/client.log" "TRACE STATE HostGameState"
shot host_settings
press A                 # START GAME is selected
wait_for "$work/client.log" "TRACE JOINED" 3 90
wait_for "$work/client.log" "TRACE STATE TeamSelectionState" 3
press DPadUp
press A
wait_for "$work/client.log" "TRACE STATE ClassSelectionState" 3
press A
wait_for "$work/client.log" "TRACE STATE MainGameState" 4
sleep 1
shot hosted_game

if grep -q "Exception" "$work/client.log" "$work/server.log"; then
    fail "an exception was logged"
fi
echo "Smoke test passed. Screenshots: $out"
