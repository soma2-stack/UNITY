# School of the Dead Overnight Summary

## Scope Completed

All four requested checkpoints were completed on branch `codex/prop-audit-checkpoints` without rewriting the project or modifying gameplay geometry.

| Checkpoint | Commit | Result |
|---|---|---|
| 1 - Prop audit | `a7827e9` | Read-only room/prop audit report |
| 2 - Room naming | `b75a812` | Safe naming and legacy-ID migration plan |
| 3 - Prop validator | `9de7f54` | Read-only placement validator and current results |
| 4 - Safe auto-fix | `6c556bb` | Confirmation-gated, Undo-enabled generated-prop fixer |

## What Changed

- Added a read-only audit covering 55 room floor IDs and 433 generated room props.
- Verified map elevation bands: basement at `Y=-4.8`, first floor at `Y=0`, and second floor at approximately `Y=4.4`.
- Added a readable alias plan for all 55 legacy room IDs.
- Added `Tools/School Of The Dead/Validate Prop Placement`.
- Added `Tools/School Of The Dead/Safe Auto Fix Props`.
- Generated current validation results from 440 checked props, 92 buyable doors, 32 stair/step/landing bounds, and 14 derived walking lanes.
- Added confirmation, Unity Undo, room-bound checks, door/wall/prop collision checks, and post-fix revalidation to the safe auto-fix workflow.

## Files Added

- `Assets/Editor/SchoolOfTheDeadPropAuditReport.md`
- `Assets/Editor/SchoolOfTheDeadRoomRenamePlan.md`
- `Assets/Editor/SchoolOfTheDeadPropPlacementValidator.cs`
- `Assets/Editor/SchoolOfTheDeadPropValidationResults.md`
- `Assets/Editor/SchoolOfTheDeadSafePropAutoFix.cs`
- This summary and the Unity `.meta` file associated with each asset

No existing scene, gameplay, player, zombie, weapon, wall, floor, stair, or door file was changed by these checkpoint commits.

## Rooms Renamed

None.

Room IDs are currently used by exact floor lookups, doorway-prefix searches, generated room containers, door exclusions, and the `starter_floor` spawn lookup. The naming document therefore keeps every legacy ID and recommends additive metadata before any hierarchy rename.

## Props Moved or Resized

None.

The safe auto-fix tool was compiled and exercised in batch dry-run mode only. It found four safe door-clearance proposals and then correctly refused to apply them in batch mode. No floor snap, wall move, scale correction, or desk rotation was proposed for the current scene.

## Reported Findings Only

The current validation report contains four errors and thirteen warnings:

- Door clearance errors: `courtyard_east/Bench1`, `courtyard_west/Planter1`, `janitors_closet_2/Crate_0`, and `janitors_closet_2/Crate_2`.
- Underground tunnel center-lane warnings: `Barrel_0`, `Crate_1`, `Generator`, `PipesN`, and `PipesS`.
- Floating visual-bound warnings: the two basement library `BookCart` props and six movable secret-book props.

The validator found no current candidates for stair blocking, doorway-transom blocking, wall clipping, wall proximity, sunken props, unrealistic scale, incorrect classroom desk/chair facing, or cafeteria/kitchen counter penetration.

## Manual Unity Review Still Required

- Inspect the four door-clearance props in Scene view before confirming the auto-fix operation.
- Walk through affected buyable doors using the 0.3 m-radius player capsule after any fix.
- Bake and inspect the NavMesh through the underground tunnel; center-lane overlap is a conservative bounds warning, not proof of blockage.
- Verify whether the library book carts intentionally omit floor-level wheel geometry.
- Place the six secret-book pickup props intentionally; their current staging positions are above the starter-room floor.
- Review generated counter front aisles even though no wall penetration was detected.
- Run a normal play-mode traversal after any scene-changing auto-fix.

## Verification

- Unity editor compilation: successful.
- Final `dotnet build Assembly-CSharp-Editor.csproj`: 0 errors, 34 pre-existing obsolete-API warnings in files outside these checkpoint tools.
- Auto-fix dry run: four door-clearance proposals; zero scene changes.
- Temporary audit/runner scripts: removed.
- Unity process: exited after verification.

## Current Git Status

- Repository: `soma2-stack/UNITY`
- Branch: `codex/prop-audit-checkpoints`
- Checkpoint commits are local and have not been pushed.
- Checkpoint-owned files are committed.
- The worktree still contains 47 modified and 12 untracked pre-existing files unrelated to these checkpoints, including `SchoolOfTheDead.unity`, `SchoolPropPlacer.cs`, animation/material assets, network/player prefabs, and player locomotion assets.
- Those unrelated changes were not staged, reverted, or included in checkpoint commits.

## Usage Status

Exact Codex account usage could not be checked because no account-level quota tool is exposed in this environment. The user reported 99% usage remaining at the start of Checkpoint 4.
