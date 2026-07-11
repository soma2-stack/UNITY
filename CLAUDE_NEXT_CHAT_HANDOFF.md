# School of the Dead — Claude Code Handoff

> Paste-in rundown for the next Claude Code chat. Read this **first** before making any changes.

## Project
- **Name:** School of the Dead
- **Type:** Unity zombie **co-op survival** game (Netcode for GameObjects, URP).
- **Current branch:** `claude/busy-allen-8wsszl`
- **Current commit:** `fb27b50` (`HUD: drop $ from points, shrink status card, grow ammo panel, tune health bar`)
- **Backup branch (frozen baseline):** `stable-playable-coop-v1`
- **Current status:** **Stable playable co-op baseline.** Treat this branch/commit as a known-good backup. Everything below is working as of this commit.

> NOTE: Claude Code runs in an **ephemeral cloud clone and cannot open Unity / compile / run the validator.** All changes so far are code-reviewed (and geometry-verified by computation for the HUD), **not editor-validated.** The user compiles/tests in Unity. When you finish work, remind the user to compile + playtest.

## What Works Now
- **Solo gameplay** (single-player runs the same scripts; networked paths gate on `IsSpawned`).
- **Host/client spawning** (players spawn, both connect).
- **Player movement / camera / audio ownership** (owner-only movement, camera, AudioListener via `NetworkPlayerAvatar`).
- **Remote player visibility** (each peer sees the other's body + nameplate; owner's own body is shadows-only first-person).
- **Shooting and zombie damage** (client-side hit detection: shooter raycasts locally, server applies authoritative damage; self-collider ignore + aim assist).
- **Zombies damaging players** (server-authoritative, per-player damage grace so a crowd can't delete you in one frame).
- **Doors** (networked open + points spend via the coordinator).
- **Weapon buying** (wall-buy: charges only on success; buy vs. ammo-refill priced from the buyer's own inventory report).
- **Muzzle particles** (each equipped weapon rebinds its own muzzle particle on equip — not just the pistol).
- **Custom runtime HUD** (`SchoolOfTheDeadHud.cs`, Canvas + TextMeshPro, built at runtime).
- **HUD art** (loads optional PNG panels from `Assets/Resources/HUD/`).
- **Revive / death / spectate** (downed→revive restores control; full death soft-despawns + spectator-camera-follows a living teammate).
- **Book pickup sync** (team-wide, server-authoritative, deduped, late-join catch-up).
- **Stairs** (walk up without jumping — single combined `Move` + adequate `stepOffset`).

## Important Recent Systems
- **`SchoolOfTheDeadHud.cs` — runtime Canvas HUD.** Self-bootstraps in the `SchoolOfTheDead` scene, builds a Canvas (Scale-With-Screen-Size 1920x1080, match 0.5) + TMP text at runtime. Panels: round (top-left), status/student-ID card (bottom-left: points, health bar, health number, DOWNED/DEAD banner), perk row (bottom-center, owned perks only), ammo (bottom-right), crosshair. **All layout is driven by tunable constants near the top of the file** (RoundPanelSize, StatusPanelSize, AmmoPanelSize, and the `Round*`/`Status*` region constants). Reads gameplay values every frame; never writes them.
- **HUD art from `Assets/Resources/HUD`.** `ApplyPanelSprite` tries candidate names (e.g. `round`/`round_panel`, `status`, `ammo`, `Perk`) and uses the first that loads; on a miss it logs one warning and keeps the placeholder color. Sprites are imported as Sprite (2D/UI), no 9-slice borders → drawn `Simple`. Points value shows the number only (no `$`); nothing is baked into the images.
- **Old HUD suppression flags.** The new HUD sets display-only kill switches once its canvas is up: `GameHud.SuppressDrawing`, `PerkManager.SuppressHud`, `PlayerHealth.SuppressHud`. Each old `OnGUI` early-returns when its flag is set; flags are cleared if the new HUD is removed, so the legacy HUD safely resumes. **Suppression is display-only — it changes no gameplay/logic.**
- **Multiplayer player spawning.** Players are the `NetworkPlayer` prefab (also used unspawned in solo). `MultiplayerSessionController` manages join/leave/restart; `NetworkGameplayCoordinator` bridges non-prefab scene systems.
- **`NetworkPlayerAvatar` — ownership / model / spectate.** Enables movement/camera/AudioListener/weapon input for the owner only; picks a body model variant per client; owner body is shadows-only. On **final death** (not merely downed) it soft-despawns (disables the CharacterController, hides renderers/nameplate on every peer) and, for the local owner, enters spectator mode: disables movement + `CoDCamera`, then in `LateUpdate` follows the nearest living teammate (throttled re-scan). Wired to `PlayerHealth.OnPlayerDied` only.
- **`WeaponController` — MP + muzzle flash.** Client-side hit detection (`ResolveShot` precise self-ignoring ray + zombie-only aim-assist sphere; `FireDamageServerRpc` applies server damage by zombie NetworkObjectId). Firing/melee blocked while `IsDowned || IsDead`. On equip, `BindMuzzleFlash` finds the spawned view model's own `ParticleSystem` (incl. inactive; prefers muzzle-named nodes) and points `SimpleGunRecoil.muzzleFlash` at it, clearing the old reference first.
- **`PlayerHealth` — downed / revive / death.** Server-authoritative with solo fallback. Downed timer synced via an absolute server-time NetworkVariable (clients run a display-only countdown). `ReviveServerRpc` is server-validated (target downed, no self-revive, reviver alive/in-range). Damage grace window caps stacked-hit chunking. Lose a random perk on down.
- **`ZombieAgent` — targeting / damage / death colliders.** Targets nearest reachable **living, non-downed** player (skips dead/downed). Attack: `attackDamage=50`, `attackInterval=1.2`, `attackRange=2`, with a line-of-sight body-block check. Points: +10 per hit, kill bonuses (body/headshot/melee) credited to the shooter. Dead zombies disable their colliders on clients too (client death-watch) so corpses don't block.
- **`NetworkGameplayCoordinator` — sync bridge.** Named-message bridge for doors, power, purchases (wall-buy/mystery box/pack-a-punch/perk), power-ups, game-over, restart, and **team-wide book collection**. Purchases are server-authoritative and only charge on success. Late-join snapshot replays door/power/box/powerup/perk/book state to a joining client.
- **`BookPickup` — team-wide sync.** Each book has a stable cross-peer key (hierarchy path + authored position). Collection routes through the coordinator; the server counts each key once for the whole team and tells every peer to hide the book + advance its local `SecretBookManager`. Solo applies locally.
- **`PlayerMovement` — stair fix.** Horizontal + vertical motion applied in a **single** `CharacterController.Move` so `stepOffset` engages and grounding stays stable on step edges; `stepOffset` raised to 0.6 (below controller height) to clear the tallest measured step (~0.45m). Fixes "have to jump up stairs."

## Files The Next Claude Chat Should Know
- `Assets/Scripts/UI/SchoolOfTheDeadHud.cs` — the current runtime Canvas/TMP HUD (all layout constants live at the top). **This is where HUD tuning happens.**
- `Assets/Scripts/UI/GameHud.cs` — legacy IMGUI HUD, now suppressed via `GameHud.SuppressDrawing`. Still holds the crosshair/points-table helpers historically; kept as a fallback.
- `Assets/Scripts/Multiplayer/Player/NetworkPlayerAvatar.cs` — per-player ownership (movement/camera/audio/weapon), model variant selection, body visibility, and death soft-despawn + spectator follow.
- `Assets/Scripts/Multiplayer/Session/MultiplayerSessionController.cs` — session lifecycle: host/join/leave, restart match, display-name handling.
- `Assets/Scripts/Multiplayer/Session/NetworkGameplayCoordinator.cs` — named-message sync for doors/power/purchases/powerups/game-over/restart/books + late-join snapshot.
- `Assets/Scripts/Player/PlayerMovement.cs` — first-person movement, crouch, footsteps, stair fix (single Move + stepOffset).
- `Assets/Scripts/Player/PlayerHealth.cs` — health, regen, downed/bleed-out, server-validated revive, death; `SuppressHud` flag.
- `Assets/Scripts/Weapons/WeaponController.cs` — weapons, client-side hit detection, server damage RPC, per-weapon muzzle flash binding, downed-gun penalty.
- `Assets/Scripts/Enemies/ZombieAgent.cs` — zombie AI targeting, attack/damage, points award, death + collider cleanup.
- `Assets/Scripts/Systems/RoundManager.cs` — round progression, spawn counts, and the in-code `CalculateZombieHealth` curve (R1 100 … R5 230). Legacy `baseZombieHealth`/`healthPerRound` fields are NOT read.
- `Assets/Scripts/GameLoop/WallBuy.cs` — wall-buy weapon purchase (charges only on success; self-corrects buy vs. ammo refill).
- `Assets/Scripts/GameLoop/MysteryBox.cs` — mystery box (gives a fresh weapon Clone; teddy-bear relocate).
- `Assets/Scripts/EasterEgg/BookPickup.cs` — collectible book with team-wide server-authoritative sync.
- `Assets/Scripts/Perks/PerkManager.cs` — owned-perk tracking + effects; custom perk names (Vital Boost, Rapid Ruin, Clip Kick, Sprint Surge, Rescue Rush, Armory Amp); `SuppressHud` flag.

## Known Things To Test Next
- **Co-op rounds 1–5** (health feel, difficulty curve, wipe risk on R5).
- **Stairs on every staircase** (incl. the irregular `stairwell`; walk up without jumping, walk down, sprint).
- **Host/client HUD values** (each sees their own health/ammo/points/perks; round + zombies-left shared).
- **Revive / death / spectate** (downed→revive restores control; full death soft-despawns + spectates a living teammate; not when merely downed).
- **Book pickup sync** (one grab advances both; 6 opens the bookcase for both; restart resets books).
- **Door prices** (verify scene doors actually charge — `Door.cost` code default is 0).
- **Zombie balance** (attack damage 50 + 0.5s grace = ~3 hits/~1.5s to down in a swarm — watch for "too cheap").
- **Weapon buying / wall buys / mystery box** (points only charged on success; correct weapon granted).
- **Duplicate camera / AudioListener warnings** (only the owner's should be enabled).

## Do Not Touch Unless I Ask
- Editor tools (`Assets/Editor/*`) and any Unity **menu/editor tools** — do not run them.
- Map generation, rooms, layout.
- Scenes (`Assets/Scenes/*`, esp. `SchoolOfTheDead.unity`) and prefabs — **never hand-edit the scene file.**
- Props / textures / materials / meta files / assets.
- Broad multiplayer refactors.
- Broad `WeaponController` refactors.
- Broad `PlayerHealth` refactors.
- Pack-a-Punch / Easter-egg expansion — leave until core gameplay is fully tested (the book **pickup sync** is done; don't expand the egg further yet).

## Safe Next Work
- Balance tuning (zombie health/damage, spawn counts, points values) — small, value-level changes.
- Door prices / progression pacing.
- Small HUD polish if needed (all via the constants at the top of `SchoolOfTheDeadHud.cs`).
- A build / testing guide (docs only).
- Simple main-menu polish.
- Pack-a-Punch — later, after core is tested.
- Easter egg — later, after core is tested.

## Starting Prompt For Next Claude Code Chat
```
Read CLAUDE_NEXT_CHAT_HANDOFF.md first before doing anything.

This branch is a STABLE PLAYABLE CO-OP BASELINE (backup branch: stable-playable-coop-v1).
Respect it: make small, targeted changes only. Do not refactor and do not add features
unless I explicitly ask.

Do NOT touch editor tools, map generation, scenes, prefabs, props, textures, or assets,
and do NOT run any Unity editor/menu tools. Do NOT hand-edit the scene file.

For any broad change to multiplayer, WeaponController, or PlayerHealth — STOP and ask me
first. You cannot compile/run Unity here, so treat changes as code-reviewed only and tell
me to compile + playtest after each change.

Today I want to work on: <fill in — e.g. balance tuning / door prices / HUD polish>.
Keep changes minimal and commit with clear messages.
```
