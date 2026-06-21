# School of the Dead — Claude Continuation Plan

Claude is taking over the prop-placement / room-organization work Codex started.
This plan records exactly where Codex stopped and what is safe to do next.

## Codex work found

- **Audit & reports** (read-only, no scene edits):
  - `SchoolOfTheDeadPropAuditReport.md` — baseline prop inventory.
  - `SchoolOfTheDeadRoomRenamePlan.md` — proposed room-ID rename plan (NOT applied; do not apply during prop work).
  - `SchoolOfTheDeadPropValidationResults.md` — validator output (4 errors, 13 warnings).
  - `SchoolOfTheDeadPropPlacementProgress.md` — full room-by-room audit, Checkpoint 1.
  - `SchoolOfTheDeadOvernightSummary.md` — overnight run summary.
- **Tools** (verified compile-clean by Claude earlier):
  - `SchoolOfTheDeadPropPlacementValidator.cs` — read-only validator (`Tools/School Of The Dead/Validate Prop Placement`).
  - `SchoolOfTheDeadSafePropAutoFix.cs` — confirmation-gated, Undo-enabled, touches only `Generated_RoomProps`
    (`Tools/School Of The Dead/Safe Auto Fix Props`). Claude already fixed its FloorSnap base-gap bug
    and Undo grouping, and removed conflict markers that a bad merge introduced.
- **Furnishing engine** already in repo: `SchoolRoomFurnisher.cs` (desks, chairs, tables, benches, counters,
  shelves, lockers, cabinets, whiteboards, beds, crates, barrels, pipes, gym mats, cones, planters, trash cans).

## Latest checkpoint found

- Codex reached **Checkpoint 1 — current placement audit only**.
- Scene edits performed: **none**. Props moved/resized/added/deleted: **none**.
- The scene is intact and verified by Claude: 11 MB YAML, 3758 GameObjects, 55 room floors,
  `Generated_RoomProps` present, ~440 props checked. (The 2.7 MB "shrunken" scene that appeared on the
  opencode/nemotron branch was **NOT** merged — that branch's scene is corrupt/gutted and must stay rejected.)

## Current validator state (the concrete to-do list)

**4 ERRORS — props in buyable-door clearance zones (safe, targeted fixes in CP2):**
1. `courtyard_east/Bench1` — in clearance of `courtyard_E_Seg0_Transom`.
2. `courtyard_west/Planter1` — in clearance of `courtyard_W_Seg0_Transom`.
3. `janitors_closet_2/Crate_0` — in clearance of paired doorway.
4. `janitors_closet_2/Crate_2` — in clearance of paired doorway.

**13 WARNINGS — handle with care, do NOT blind-fix:**
- 5 `underground_tunnel` props (Barrel_0, Crate_1, Generator, PipesN, PipesS) overlap a *conservative*
  1.8 m center lane. This is a heuristic, not a NavMesh result — verify before moving; likely leave.
- 2 `BookCart` meshes float 0.43 m — probably missing wheel/base geometry in the model, NOT a bad root.
  Do not lower the root.
- 6 `Generated_SecretEgg` books float ~1.09 m — **intentional** Easter-egg staging; do not floor-snap.

## Safe work remaining (in checkpoint order)

- **CP2 — obvious bad placement only:** nudge the 4 door-blocking props out of their clearance zones
  (Bench1, Planter1, Crate_0, Crate_2) using small, in-room moves. Re-run the validator to confirm 0 door errors.
  Leave the tunnel/bookcart/secret-book warnings as documented.
- **CP3 — classrooms & offices:** classrooms already pass facing/scale/clip checks; refinements only
  (row spacing, restrained wall dressing). Under-furnished targets: `main_office` (1 prop), `west_south_office`,
  `principal_office`.
- **CP4 — cafeteria & kitchen:** verify lanes / walk-behind clearance; reposition only if a capsule path fails.
- **CP5 — gym, hallways, stairs:** `gym` (4 props) and empty hallways need wall-biased dressing; stairs stay clear.
- **CP6 — full validation + final summary.**

## Risky work to AVOID

- Do NOT apply `SchoolOfTheDeadRoomRenamePlan.md` (room-ID renames would desync the furnisher/validator/secret-egg).
- Do NOT touch map geometry: walls, floors, ceilings, stairs, doors, room shapes, gameplay machines.
- Do NOT blind floor-snap the secret-egg books or the BookCarts (warnings are expected/intentional).
- Do NOT move the underground_tunnel utility props without a real NavMesh/capsule test.
- Do NOT merge anything from the opencode/nemotron scene (it is gutted).
- Do NOT mass-rerun the furnisher (it rebuilds layouts from scratch and would discard hand-tuning).

## Checkpoint 2 — DONE (obvious prop placement fixes)

Resolved all **4 door-clearance errors** with minimal in-room nudges. Root cause: the courtyard
"buyable doors" are 24 m-long panels spanning the whole courtyard edge, so their 0.9 m clearance zone
reaches deep into the courtyard where the bench/planter sat. The janitor crates flanked a narrow closet door.

Moves applied (only `Generated_RoomProps` transforms changed; nothing else touched):

| Prop | Room | Old local pos | New local pos | Move |
|---|---|---|---|---|
| `Bench1` | courtyard_east | (-22.75, 0, 38.08) | (-21.0, 0, 38.08) | +1.75 m east into courtyard |
| `Planter1` | courtyard_west | (-41.3, 0, 37.83) | (-42.5, 0, 37.83) | -1.2 m west into courtyard |
| `Crate_0` | janitors_closet_2 | (-74.9, 0, -18.37) | (-73.4, 0, -18.37) | +1.5 m east into closet |
| `Crate_2` | janitors_closet_2 | (-74.9, 0, -12.97) | (-73.4, 0, -12.97) | +1.5 m east into closet |

Verified by re-parsing the scene: every moved prop's full world AABB now clears its door's 0.9 m zone by
>0.9 m, stays inside its room floor, and stays on the ground (Y unchanged). Scene integrity intact
(3758 GameObjects, unchanged). The crates land in an empty gap between the west-wall door and the
east-side shelves/mop bucket — no prop-prop clipping.

**Not touched (still documented warnings, intentionally left):** 5 underground_tunnel props (need a real
NavMesh test), 2 floating BookCarts (missing wheel geometry), 6 secret-egg books (intentional staging).

> Note: the validator could not be re-run here (no Unity in this environment). The 4 door errors were
> resolved by direct geometric verification. Re-run `Tools/School Of The Dead/Validate Prop Placement`
> in Unity to refresh `SchoolOfTheDeadPropValidationResults.md` (expected: 0 errors, ~13 warnings).

## Checkpoint 3 — DONE (classrooms & offices)

**Classrooms:** already furnished and passing every validator check (16–17 props each: whiteboard,
teacher desk/chair, student desks/chairs facing the board, shelf, trash). The audit marked them
"No move indicated." With no Unity here to visually refine, re-laying-out passing rooms would only add
risk, so classrooms are **left as-is** (correct per the "don't touch what passes" rule).

**Offices:** the real gap. Root cause of `main_office` having ~1 prop: the old `FurnishOffice` hardcoded
`Wall.North`/`Wall.West`, so when those walls held doorways every placement failed the door-clearance
`Fits()` test. Fixed in code:

- Rewrote `FurnishOffice` to be orientation-robust: main desk goes against the wall *farthest from any
  doorway* (`ChooseFrontWall`), with a fallback that tries the other walls if the room is tight. Added a
  desktop monitor, a guest chair facing the desk, filing cabinets on a door-clear side wall
  (`ChooseSideWall`, new), a shelf, and a trash can — a believable, non-crowded office.
- Added a **surgical** menu: `Tools/School Of The Dead/Furnish Offices (Safe, Offices Only)`
  (`FurnishOfficesOnly`). Unlike "Furnish Rooms" (which rebuilds the entire `Generated_RoomProps` root
  from scratch and would wipe the CP2 fixes), this rebuilds **only** `main_office` and `principal_office`
  in place and leaves every other room and all hand-tuned positions untouched. `west_south_office` is
  intentionally excluded (already usable, 6 props).

> ACTION REQUIRED IN UNITY: this checkpoint changed **code only** — the scene is unchanged. To apply the
> office furniture, open the project and click **Tools ▸ School Of The Dead ▸ Furnish Offices (Safe,
> Offices Only)** once. It opens the scene, refurnishes the two sparse offices, and saves. (I can't run
> Unity in this environment, so I can't apply it for you, and hand-authoring dozens of desk/chair
> GameObjects into the scene YAML would risk corrupting it.)

## Checkpoint 4 — DONE (cafeteria & kitchen)

Measured both rooms' actual layouts directly from the scene YAML (world positions + floor extents):

**Kitchen (`cafeteria_kitchen`, 15.2 × 10.4 m):** fully and believably furnished — prep counters along the
north and south walls, a central prep island, and storage shelves down the west wall. Walk-behind aisles
measure ~3 m (counter front → island), well above the 0.3 m player radius. **No changes needed; left as-is.**

**Cafeteria (`cafeteria`, 24 × 20 m):** found a real believability gap — its container holds **only the
`FoodCounter_*` serving line; all dining tables, benches, the trash can and tray crate are missing** (the
room was furnished by an older pass). The current `FurnishCafeteria` code already produces neat parallel
table+bench rows kept clear of the service line and doorways, so the fix is to refurnish the cafeteria
in place.

Code changes:
- Refactored the office-only tool into a reusable, type-aware `RefreshRoomsInPlace(ids)` that furnishes
  each named room via `Dispatch` (so cafeteria→`FurnishCafeteria`, office→`FurnishOffice`, etc.), clearing
  only that room's container and leaving everything else untouched.
- `FurnishOfficesOnly` now calls it; added `FurnishCafeteriaOnly`
  (`Tools ▸ School Of The Dead ▸ Furnish Cafeteria (Safe, Cafeteria Only)`).

> ACTION REQUIRED IN UNITY: code-only changes again. Click **Tools ▸ School Of The Dead ▸ Furnish
> Cafeteria (Safe, Cafeteria Only)** once to add the dining tables/benches/trash to the cafeteria in
> place. (Kitchen needs nothing.)

## Suggested next checkpoint

**Checkpoint 5 — gym, hallways, stairs:** gym has only 4 props (under-dressed); hallways are empty and need
wall-biased dressing (lockers/signs/benches) without blocking center lanes; stairs must stay clear.

## Working state

- Branch: `claude/school-of-the-dead`. CP4 changed `SchoolRoomFurnisher.cs` only (code; scene untouched).
  Brace-balanced (233/233), new menus verified.
- Safe to continue.
