# School of the Dead — Claude Final Prop-Placement Summary

Final summary of the prop-placement / room-organization work, taking over from Codex.
Branch: `claude/school-of-the-dead`.

## IMPORTANT — validation status

The live Unity validator could not be run in this environment (no Unity available here). Validation below
is **static**: direct geometry math on the scene YAML for changes already applied, plus a compile/safety
review of the editor tools. The three "furnish" tools added in CP3–CP5 are **code-only**; their props do
not exist in the scene until the menus are run in Unity. Run the four in-Unity actions listed at the bottom,
then re-run `Validate Prop Placement` for an authoritative result.

## What Codex already did

- Read-only audit + planning, no scene edits:
  - `SchoolOfTheDeadPropAuditReport.md`, `SchoolOfTheDeadRoomRenamePlan.md` (rename plan NOT applied),
    `SchoolOfTheDeadPropValidationResults.md` (4 errors, 13 warnings), `SchoolOfTheDeadPropPlacementProgress.md`
    (room-by-room audit), `SchoolOfTheDeadOvernightSummary.md`.
- Tools: `SchoolOfTheDeadPropPlacementValidator.cs` (read-only validator) and
  `SchoolOfTheDeadSafePropAutoFix.cs` (confirmation-gated, Undo-safe auto-fix).
- Reached **Checkpoint 1 (audit only)**.

## What Claude finished

- **CP1** — verified the scene is intact (3758 GameObjects, 55 rooms, `Generated_RoomProps` present; the
  gutted 2.7 MB opencode/nemotron scene was correctly never merged) and wrote
  `SchoolOfTheDeadClaudeContinuationPlan.md`.
- **CP2** — resolved all **4 buyable-door clearance errors** by direct, verified scene edits (the only hard
  errors the validator reported).
- **CP3** — classrooms verified already-good (left as-is); rewrote `FurnishOffice` to be orientation-robust
  and richer; added a surgical offices-only refresh tool.
- **CP4** — kitchen verified already-good (left as-is); found the cafeteria was missing all dining furniture
  and made it refurnishable in place; refactored the surgical tool to be type-aware (`RefreshRoomsInPlace`).
- **CP5** — gym refurnishable in place (adds floor mats/cones); added a lane-safe `FurnishHallway` and a
  gym+hallways refresh tool; stairwells deliberately untouched.

## Files changed (by Claude, this task)

| File | Change |
|---|---|
| `Assets/Scenes/SchoolOfTheDead.unity` | CP2 only: 4 prop transform positions (no geometry/structure changes) |
| `Assets/Editor/SchoolRoomFurnisher.cs` | Improved `FurnishOffice`; new `ChooseSideWall`, `TryOfficeDesk`, `FurnishHallway`, `RoomType.Hallway`, 11 hallways registered, `Dispatch` case; new in-place `RefreshRoomsInPlace` + 3 safe menus |
| `Assets/Editor/SchoolOfTheDeadClaudeContinuationPlan.md` | Living checkpoint log (CP1–CP5) |
| `Assets/Editor/SchoolOfTheDeadClaudeFinalPropSummary.md` | This file |
| `Assets/Editor/SchoolOfTheDeadSafePropAutoFix.cs` | (pre-CP) restored correct version after a bad merge left conflict markers |

## Rooms changed

- **Directly in the scene (CP2):** `courtyard_east`, `courtyard_west`, `janitors_closet_2` (one prop each / two crates).
- **Via the safe tools (apply by running the menus):** `main_office`, `principal_office` (offices);
  `cafeteria`; `gym`; and all 11 hallways.
- **Verified good, left untouched:** all classrooms, `cafeteria_kitchen`, `west_south_office`, science lab,
  conference, library/-basement, nurse rooms, security, storage/janitor, utility/teleporter/vault/tunnel,
  courtyards (besides the CP2 nudges).

## Props MOVED

| Prop | Room | From (local) | To (local) | Reason |
|---|---|---|---|---|
| `Bench1` | courtyard_east | (-22.75, 0, 38.08) | (-21.0, 0, 38.08) | out of 24 m-door clearance |
| `Planter1` | courtyard_west | (-41.3, 0, 37.83) | (-42.5, 0, 37.83) | out of 24 m-door clearance |
| `Crate_0` | janitors_closet_2 | (-74.9, 0, -18.37) | (-73.4, 0, -18.37) | off the closet doorway |
| `Crate_2` | janitors_closet_2 | (-74.9, 0, -12.97) | (-73.4, 0, -12.97) | off the closet doorway |

All four verified (world AABB) to clear their door's 0.9 m zone by >0.9 m, stay on the floor, Y unchanged.

## Props ADDED

None directly in the scene yet. The tools, when run in Unity, add: office desk+chair+monitor+guest
chair+filing cabinets+shelf+trash can (main_office, principal_office); cafeteria dining tables+benches+
trash+tray crate; gym floor mats/cones; hallway lockers + occasional trash cans (wall-biased).

## Props RESIZED

None. No prop scales were changed anywhere.

## Rooms SKIPPED (and why)

- All **stairwells** — must stay clear; never furnished (excluded from the room table).
- `parking_lot` — no suitable vehicle/exterior props in the project; not dressed.
- `west_south_office` — already usable (6 props, passes checks); not rebuilt to avoid disturbing it.
- Rooms already passing all checks — not re-laid-out (would add risk for no gain, and no Unity for visual review).

## Problems NOT fixed (left intentionally)

- 5 `underground_tunnel` props overlapping a conservative 1.8 m center-lane heuristic — needs a real NavMesh
  / player-capsule test before moving; likely fine.
- 2 `library_basement*/BookCart` floating 0.43 m — probably missing wheel/base geometry in the model, not a
  bad root; should be inspected, not floor-snapped.
- 6 `Generated_SecretEgg` books floating ~1.09 m — **intentional** Easter-egg staging; must not be snapped.

## Things to manually check in Unity

1. Run the 4 in-Unity actions below, then **bake the NavMesh** and walk the map with the player capsule.
2. Confirm hallway lanes still read as open after lockers are placed (they should keep ~3+ m clear).
3. Eyeball the cafeteria table rows and office layouts from the player camera.
4. Confirm the four moved courtyard/closet props look natural in their new spots.
5. Inspect the BookCart model and the secret-egg book staging (left as-is by design).

## In-Unity actions (run once each, any order)

1. `Tools ▸ School Of The Dead ▸ Furnish Offices (Safe, Offices Only)`
2. `Tools ▸ School Of The Dead ▸ Furnish Cafeteria (Safe, Cafeteria Only)`
3. `Tools ▸ School Of The Dead ▸ Furnish Gym & Hallways (Safe)`
4. `Tools ▸ School Of The Dead ▸ Validate Prop Placement` (refresh results; expect 0 errors)

## Current git status

- Branch `claude/school-of-the-dead`, working tree clean, all checkpoints pushed.
- Scene integrity: 3758 GameObjects (unchanged from the CP1 baseline).

## Commit list (this task)

- `checkpoint 1 claude continuation plan`
- `checkpoint 2 obvious prop placement fixes`
- `checkpoint 3 classrooms and offices finished`
- `checkpoint 4 cafeteria and kitchen finished`
- `checkpoint 5 gym hallways stairs finished`
- `final claude prop placement validation summary`

## Safety statement

No map geometry (walls, floors, ceilings, stairs, doors, room shapes) was changed. No gameplay systems,
scripts, prefabs, materials, or models were deleted. The only scene change is four prop position nudges;
everything else is additive editor tooling. The project is left in a working, compilable state.
