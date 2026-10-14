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

## 5. Gameplay depth
- [ ] Match timer / win target scaled to player count
- [ ] Team auto-balance
- [ ] Larger worlds (64³ is baked into the protocol)
- [ ] Better player collision (currently three sample points)

## 6. Feel and polish
- [ ] Instant local feedback for digging and building
- [ ] Stereo (panned) positional sound
- [ ] Better looking translucent force fields
- [ ] Colour-blind friendly team colours

## 7. Admin and distribution
- [ ] Ban by name, remote admin with a password
- [ ] `load` resends the map to connected players
- [ ] Packaging: signed macOS .app, Linux AppImage/Flatpak, Windows installer, server Docker image

## 8. Code health
- [ ] Break up `PropertyBag`, type-safe game states
- [ ] Client tests
