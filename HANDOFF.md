# School of the Dead — Session Handoff

**Repo:** `soma2-stack/UNITY` · **Branch:** `claude/school-of-the-dead` · **Engine:** Unity 6 (URP), CoD-Zombies-style.
**Latest commit at handoff:** `a0ef39a` (perk rename). Always `git pull origin claude/school-of-the-dead` first.

## Environment / hard constraints
- Assistant works in an **ephemeral cloud clone — CANNOT run Unity / compile / play-test.** Verify code by brace-balance + grep for dangling refs; the **user runs Unity** and reports errors/feel.
- **Legacy Input Manager** (not new Input System). Use `FindFirstObjectByType`/`FindObjectsByType` (never obsolete `FindObjectOfType`).
- Don't edit `.meta` files except creating a 2-line meta (`fileFormatVersion: 2` + a fresh `guid`) for NEW scripts.
- **Never hand-edit the scene file** `Assets/Scenes/SchoolOfTheDead.unity` (huge YAML; was gutted once). Scene changes go through editor tools only.
- Commit trailers: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` + `Claude-Session: ...`. Push with retries; if rejected, `git fetch` + `git merge origin/...` then push.

## Workflow rules the user set
- Multiple AIs touch this repo (opencode + Ollama Qwen/DeepSeek). Keep each on its **own branch**; assistant integrates. One owner per file. No autonomous loops.
- **"Compare and pick the best / avoid duplication"** when a prompt would recreate something that exists (caused dup `ZombieSpawner`/`PerkMachine`/`MeleeWeapon` before).
- User is **usage-conscious** — batch work, don't re-read files already known.

## Current state (all CoD-accurate, audited clean)
Working & solid: points economy (+10 hit / +60 body / +100 headshot / +130 melee, `ZombieAgent` owns it), downing/bleedout/revive (loses a random perk), perks (6, 4-perk cap, **renamed**: VitalBoost/ClipKick/RapidRuin/SprintSurge/ArmoryAmp/RescueRush — RapidRuin also doubles bullet dmg), powerups (weighted drops, nuke flash, teddy-bear box relocate), rounds (count +2 cap 24; health +50/round to R9 then x1.1; speed +0.1 cap 6; 10s intermission countdown banner), doors (cost + reward + navmesh carving + repath), wall buy (reserves only), Pack-a-Punch (`isUpgraded` flag), weapons (M1911 default, mule-kick slots, knife on V), HUD (single `GameHud` owns crosshair/ammo/round/points), game over (best round/score/kills via PlayerPrefs).
Recently added: **hit marker** (`UI/HitMarkerHud.cs`, self-bootstraps), **blood effect** (`WeaponController.bloodHitEffect` field, zombie-only), recoil/muzzle/sound via merged `SimpleGunRecoil` (re-binds on weapon switch in `EquipCurrent`).

## Known open items / things only the user can do in Unity
1. **Animator param mismatch** — `PlayerMovement` writes `MoveX/MoveZ/IsMoving/IsSprinting/Jump`; `PlayerAnimator`/`NetworkPlayerAvatar` use `Speed/Sprint/Crouch`. Reconcile to whatever `PlayerAnimator.controller` actually defines (assistant can't see it reliably).
2. **`SecretBookManager` is NOT self-bootstrapping** — must be placed in the scene or the book easter egg silently does nothing.
3. **Inspector setup needed:** assign `Blood Hit Effect` prefab on WeaponController; on the gun's `SimpleGunRecoil` assign muzzleFlash/gunAudio/gunshotSound; verify `RoundManager.timeBetweenRounds = 10` and `powerupDropPoint`; Mystery Box `weaponPool` models + `boxLocations`; `ZombieSpawner.spawnPoints`; bake NavMesh with doorways open.
4. **Cosmetic leftover:** PerkManager `[Header("Juggernog")]`-style labels + field names (`juggernogMaxHealth`, `speedColaReloadMultiplier`, ...) still use old perk names (kept to preserve serialized values).
5. Pending editor tools to run once: `Furnish Offices / Cafeteria / Gym & Hallways`, `Validate Prop Placement`.

## Recovery refs (cloud clone only, not pushed)
`backup-before-reset` and `cod-pipeline-b045757` (44 discarded "CoD Pipeline" commits) exist locally if ever needed.
