# Infiniminer roadmap

Improvements planned for the modern port (`src/`). Items are worked roughly in this order; tick them off as
they land.

## 1. Server authority (anti-cheat) — done
The original server trusted almost everything the client said.
- [x] Server-side movement validation (speed limits, no walking through blocks) with position corrections
- [x] Server decides respawn positions
- [x] Server-side deaths: lava, shock blocks, explosions, falling, falling off the map
- [x] Tools and blocks validated against the player's class and team; tool cooldowns enforced
- [x] Bank deposits/withdrawals only next to your own team's bank
- [x] Team/class changes only while dead (changing team kills you, as the client flow already did)
- [x] Rate limits for chat, pings and client-triggered sounds; pings can't impersonate other players
- [x] Tool use uses the server's idea of where the player is
- [x] Admin `tp <player> <x> <y> <z>` command

## 2. Settings and display — done
- [x] In-game settings screen (name, resolution, fullscreen, volume, mouse sensitivity, invert Y, FOV, bloom, FPS counter)
- [x] Settings saved to client.config.txt
- [x] Rebindable keys
- [x] Resizable window; menus scale to fit the window
- [x] HUD scaling for high-resolution screens

## 3. Unfinished features from the original — done
- [x] Teleporter blocks: engineers build them (200 ore, the price on the original icon); stepping onto one
      takes you to the next teleporter your team built, in build order
- [x] Shock blocks kill on touch: `shocktouch = true` server option (off by default, as in v1.5)
- [x] Team home bases: `homebases = true` server option; a crater with a metal floor and the team's base
      terminal in opposite corners, shown as HOME on the radar; players spawn there
- [x] Drill tool: miners carry it; slower than the pickaxe, but bores through rock
- [x] Metal block: the indestructible floor of home bases

## 4. Finding games — done
- [x] A replacement public server list service (`src/Infiniminer.ServerList`): the original /post and /plain
      protocol plus a port field, expiry, per-address limits, and a web page listing the servers

## 5. Gameplay depth — done
- [x] Match timer (`timelimit`, with overtime on a tie) and win target scaled to player count
      (`winningcashperplayer`), shown on the HUD and scoreboard
- [x] Team auto-balance (`autobalance`)
- [x] Larger worlds: `mapsize` = 64, 96 or 128; the server sends the size first and map chunks are compressed
- [x] Better player collision: the body has width (corners of a 0.4-block footprint are tested), so it can't
      slip through diagonal gaps or press the camera into walls

## 6. Feel and polish — done
- [x] Instant local feedback for digging and building (predicted, rolled back if the server refuses)
- [x] Stereo (panned) positional sound
- [x] Force fields shimmer gently
- [x] Colour-blind mode (Okabe-Ito team colours, [R]/[B] chat tags); team headings on the scoreboard

## 7. Admin and distribution
- [x] Kick/ban by name, unban, and remote admin from chat (`adminpassword`, `/login`)
- [x] `load` resends the map to connected players (map files are restricted to plain names)
- [x] Packaging (`packaging/package.sh`): Windows installer and portable zip, macOS .app (ad-hoc signed),
      Linux tarball with a menu entry script, Docker images for the server and the server list
- [x] Installed copies keep settings, bans and saves in a per-user folder
- [x] CI packages every platform on each push; version tags publish a GitHub release and the images
- [ ] Later: notarised macOS builds and a signed Windows installer (need paid certificates)
- [x] Flatpak bundle (`packaging/flatpak`), built and start-checked in CI and attached to releases; publishing
      it on Flathub would be a separate submission

## 8. Code health
- [x] Type-safe game states (`typeof(MainGameState)` instead of strings resolved by reflection)
- [x] Break up `PropertyBag` and `InfiniminerGame`: client→server encoding (`ClientMessages`, shared with the
      tests' bot), server message handling (`ServerMessageHandler`), `ChatLog`, `Radar`, `SoundPlayer`, and dig
      rules shared by the server and the client's prediction (`GameRules.Dig`)
- [x] Client tests: radar, chat, sound panning, dig rules, wire format, state construction
- [x] Moved the local player's fields out of `PropertyBag` (see section 9)

## 9. Next
- [x] Controller support: analog movement and look, hold RT to dig, rumble, every menu navigable with the
      D-pad/stick (and the keyboard), an in-game menu (Escape or Start), prompts that follow the device in use,
      controller sensitivity and vibration options, and a controller layout page
- [x] Host a game from the client: HOST A GAME in the server list runs a server inside the game (world size,
      players, time limit, lava, home bases, sandbox), LAN players find it as usual, END GAME stops it
- [x] Recent servers in the server list (the last five joined), and rejoining automatically when a server
      restarts with a new map after a match
- [x] The local player's state and rules (equipment cycling, class and team changes) live in `LocalPlayer`
- [x] Linux AppImage (`Infiniminer.AppImage`, or `Infiniminer.AppImage server` for the dedicated server)

## 10. After that
- [x] Automated play test in CI (`tests/e2e/smoke.sh`): the real game and server under a virtual display,
      driven by a scripted controller, with a screenshot of every step; `--connect <address>` and trace output
- [x] Computer players (`bots` setting and command, COMPUTER PLAYERS in HOST GAME): miners that dig, tunnel for
      treasure and bank it, keep teams even and make room for people
- [x] Internet play: hosted games and dedicated servers can ask the router to forward their port (UPnP), show
      the address to give out, and say plainly when the router won't
- [x] Quick team chat with a controller (hold LT, press a D-pad direction)
- [x] Polish: rejoining after a restart keeps your team and class; clouds as bright as the original shader made
      them (the port had them at full white, which the bloom blew out); the team screen no longer shows the
      server list's heading and buttons from the original artwork

## 11. Bots and the Steam Deck
- [x] Bots of every class: prospectors sense treasure further away; engineers gather ore, climb out on ladders,
      build the team a bank and stock it; sappers blast through rock from a safe distance
- [x] Smarter bots: flee lava, take diamonds home straight away, find another way up when climbing stalls
- [x] Steam Deck readiness: an on-screen keyboard for controller text entry (names, addresses, chat), chat
      from the controller, 1280x800 on first run on a Deck, setup notes; fullscreen is now borderless
- [ ] Try it on a real Steam Deck

## 12. More to play with
- [x] Bot difficulty: easy, normal or hard (`botskill`, the console command, COMPUTER SKILL in HOST GAME)
- [x] Keep the world between sessions until a match ends (`persist`, KEEP WORLD and CONTINUE GAME in HOST GAME)
- [x] Faster rendering: faces pointing away from the camera aren't drawn (about half the draw calls and
      triangles, identical picture); `seed` for repeatable worlds; frame rate and draw counts in the trace output
- [x] Spectator mode: SPECTATE on the team screen, free flight, "WATCHING" on the scoreboard; the server
      won't respawn or accept actions from players without a team and class
- [x] Bots that defend and attack: engineers fence the bank in with force fields (two high, so it can't be
      jumped) and then a rail of shock blocks above it (no room to climb over), and sappers raid the enemy
      bank and blast through its fence
