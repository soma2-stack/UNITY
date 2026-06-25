# School of the Dead Prop Audit Report

Generated from a read-only Unity Editor inspection of `Assets/Scenes/SchoolOfTheDead.unity`. No scene objects, transforms, geometry, or gameplay systems were changed.

## Audit Scope

- Scene roots: 12
- Scene transforms inspected: 3716
- Mesh renderers inspected: 3098
- Room floor IDs detected: 55
- Generated room containers detected: 37
- Generated top-level props detected: 433
- Exact Codex account usage/remaining hours are not exposed by the available app tools.

## Priority Findings

- No confirmed extreme-scale, stair-blocking, or wall-clipping defects were detected by the conservative bounds audit.
- Two pipe runs (`the_vault/PipesS` and `underground_tunnel/PipesN`) are near the same doorway audit point and require a Scene-view/player-capsule check before any movement.
- Whiteboards, security monitors, and utility pipes are intentionally elevated by `SchoolRoomFurnisher`; they are not floating-prop defects.
- The two library book carts have their lowest rendered shelf 0.43 m above the floor because their generated wheel geometry is below/around the shelf structure. Review visually, but do not auto-snap from renderer bounds alone.
- Walking-path clearance, desk facing, and buyable-door collider clearance cannot be proven from the current scene metadata and remain manual-review items.

## Scene and Asset Organization

- Main gameplay scene: `Assets/Scenes/SchoolOfTheDead.unity` (enabled in Build Settings).
- Menu scene: `Assets/Scenes/MainMenu.unity`.
- Gameplay prefabs: `Assets/Prefabs` (`Player`, `Player_Gunman`, `Player_Outlow`, and `Zombie`).
- Runtime network prefab: `Assets/Resources/NetworkPlayer.prefab`.
- Generated furnishing root: `Generated_RoomProps`; legacy prop root: `Generated_Props`; door root: `Generated_Doors`.
- Placement tooling: `SchoolRoomFurnisher`, `SchoolPropPlacer`, `SchoolDoorPlacer`, `SchoolGameplaySetup`, `SecretStaircaseSetup`, `PerkMachinePlacer`, and `GameLoopMachinePlacer`.

## Rooms

| Current room ID | Suggested readable identity | Category | Generated props | Audit note |
|---|---|---:|---:|---|
| `cafeteria` | Cafeteria | Cafeteria | 16 | Floor detected |
| `cafeteria_kitchen` | Cafeteria Kitchen | Kitchen | 27 | Floor detected |
| `cafeteria_west_hallway` | Cafeteria West Hallway | Hallway | 0 | Floor detected |
| `conference_room` | Conference Room | Conference | 10 | Floor detected |
| `courtyard` | Courtyard | Courtyard | 2 | Floor detected |
| `courtyard_east` | Courtyard East | Courtyard | 3 | Floor detected |
| `courtyard_west` | Courtyard West | Courtyard | 2 | Floor detected |
| `east_room_1` | East Room 1 | Classroom | 17 | Floor detected |
| `east_room_2` | East Room 2 | Classroom | 17 | Floor detected |
| `east_room_3` | East Room 3 | Classroom | 16 | Floor detected |
| `east_stairwell` | East Stairwell | Stairwell | 0 | Floor detected |
| `gym` | Gym | Gym | 4 | Floor detected |
| `gym_north_hallway` | Gym North Hallway | Hallway | 0 | Floor detected |
| `hallway` | Hallway | Hallway | 0 | Floor detected |
| `history_room` | History Room | Classroom | 16 | Floor detected |
| `janitors_closet` | Janitors Closet | Storage | 12 | Floor detected |
| `janitors_closet_2` | Janitors Closet 2 | Storage | 6 | Floor detected |
| `library` | Library | Library | 14 | Floor detected |
| `library_basement` | Library Basement | Library | 15 | Floor detected |
| `library_basement_annex` | Library Basement Annex | Library | 15 | Floor detected |
| `library_staircase` | Library Staircase | Library | 0 | Floor detected |
| `lower_hallway_east` | Lower Hallway East | Hallway | 0 | Floor detected |
| `lower_hallway_south` | Lower Hallway South | Hallway | 0 | Floor detected |
| `main_office` | Main Office | Office | 1 | Floor detected |
| `math_room` | Math Room | Classroom | 16 | Floor detected |
| `music_room` | Music Room | Classroom | 16 | Floor detected |
| `nurses_office` | Nurses Office | Nurse/medical | 6 | Floor detected |
| `nurses_office_backroom` | Nurses Office Backroom | Nurse/medical | 4 | Floor detected |
| `parking_lot` | Parking Lot | Exterior | 0 | Floor detected |
| `principal_office` | Principal Office | Office | 3 | Floor detected |
| `science_lab` | Science Lab | Science lab | 14 | Floor detected |
| `security_room` | Security Room | Security | 8 | Floor detected |
| `south_end_hallway` | South End Hallway | Hallway | 0 | Floor detected |
| `south_office_classroom_1` | South Office Classroom 1 | Classroom | 17 | Floor detected |
| `south_office_classroom_2` | South Office Classroom 2 | Classroom | 16 | Floor detected |
| `south_office_classroom_3` | South Office Classroom 3 | Classroom | 16 | Floor detected |
| `south_office_classroom_4` | South Office Classroom 4 | Classroom | 16 | Floor detected |
| `south_office_hallway` | South Office Hallway | Hallway | 0 | Floor detected |
| `staff_entrance_alley` | Staff Entrance Alley | Exterior/circulation | 0 | Floor detected |
| `staff_entrance_alley_north` | Staff Entrance Alley North | Exterior/circulation | 0 | Floor detected |
| `stairwell` | Stairwell | Stairwell | 0 | Floor detected |
| `stairwell_2` | Stairwell 2 | Stairwell | 0 | Floor detected |
| `starter` | Starter | Classroom | 16 | Floor detected |
| `teleporter_room` | Teleporter Room | Utility/gameplay | 10 | Floor detected |
| `the_vault` | The Vault | Utility/gameplay | 10 | Floor detected |
| `underground_tunnel` | Underground Tunnel | Utility/circulation | 5 | Floor detected |
| `upper_hallway` | Upper Hallway | Hallway | 0 | Floor detected |
| `upper_hallway_2` | Upper Hallway 2 | Hallway | 0 | Floor detected |
| `upper_hallway_3` | Upper Hallway 3 | Hallway | 0 | Floor detected |
| `upper_hallway_north` | Upper Hallway North | Hallway | 0 | Floor detected |
| `west_cafeteria_classroom_1` | West Cafeteria Classroom 1 | Classroom | 17 | Floor detected |
| `west_classroom_1` | West Classroom 1 | Classroom | 17 | Floor detected |
| `west_classroom_2` | West Classroom 2 | Classroom | 16 | Floor detected |
| `west_south_office` | West South Office | Office | 6 | Floor detected |
| `west_wing_storage` | West Wing Storage | Storage | 11 | Floor detected |

## Confusing or Fragile Naming

- Directional IDs such as `east_room_1`, `east_room_2`, and `east_room_3` do not describe room purpose or floor.
- `starter` describes progression rather than architecture; a readable room name should retain `starter` as a legacy ID.
- `west_cafeteria_classroom_1` and `south_office_classroom_*` encode adjacency in long IDs, but not floor level.
- `the_vault`, `teleporter_room`, and `underground_tunnel` are meaningful gameplay IDs and should not be automatically renamed without a reference audit.
- Generated props contain repeated primitive child names (`Top`, `Leg`, `Seat`, and similar). This is acceptable inside a uniquely named parent but confusing in global hierarchy searches.

## Prop Categories

- Classroom furniture: 221 top-level generated prop(s)
- Storage and shelving: 81 top-level generated prop(s)
- Other room dressing: 47 top-level generated prop(s)
- Kitchen/counter fixtures: 37 top-level generated prop(s)
- Utility/storage props: 26 top-level generated prop(s)
- Wall/display props: 15 top-level generated prop(s)
- Medical furniture: 4 top-level generated prop(s)
- Tables and seating: 2 top-level generated prop(s)

## Door Clearance Candidates

- `the_vault/PipesS` is within the 1.2 m doorway audit zone at (-48.20, -1.50, 47.69).
- `underground_tunnel/PipesN` is within the 1.2 m doorway audit zone at (-48.20, -1.50, 47.69).

## Elevated Prop Review

- 15 classroom/lab whiteboards are intentionally wall-mounted about 0.76 m above the floor and have no colliders.
- `security_room/Monitor0` and `Monitor1` are intentionally placed on the security desk at about 0.75 m.
- Six utility pipe runs are intentionally wall-mounted about 1.04 m above their room floors and retain colliders as obstacles.
- `library_basement/BookCart` and `library_basement_annex/BookCart` have rendered shelf bounds starting 0.43 m above the floor. Inspect the wheel/base silhouette manually; do not snap automatically.

## Scale Candidates

- No candidates detected by this conservative bounds-based audit.

## Stair Blocking Candidates

- No candidates detected by this conservative bounds-based audit.

## Wall Intersection Candidates

- No candidates detected by this conservative bounds-based audit.

## Validation Gaps Requiring Manual Unity Review

- Main walking paths are not represented by dedicated path volumes or tags, so path blocking cannot be proven safely from names and renderer bounds alone.
- Desk/chair facing cannot be validated reliably because rooms do not expose persistent teacher/front markers. `SchoolRoomFurnisher` derives a front wall from doorway geometry at generation time.
- Renderer-bound overlap is only a candidate signal. Wall-backed shelves, counters, posters, and furniture legs can produce intentional overlaps.
- Buyable-door clearance should be validated against the actual `Door` collider and player capsule in Scene view or a later purpose-built validator.
- NavMesh walkability and stair traversal require a bake/runtime check; this audit did not rebake navigation.

## Existing Placement Safety

- `SchoolRoomFurnisher` uses world-space floor bounds, a 0.8 m wall inset, and a 1.8 m doorway clear radius.
- Its generated content is isolated under `Generated_RoomProps` and rebuilt deterministically.
- `SchoolPropPlacer` now redirects its legacy menu command to the room furnisher.
- These safeguards reduce risk but do not replace collider, NavMesh, and visual validation.
