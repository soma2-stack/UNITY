# School of the Dead Room Naming Plan

## Decision

Checkpoint 2 is plan-only. No scene objects, scripts, classes, prefabs, or generated room containers were renamed.

Automatic renaming is not currently safe because room IDs are operational identifiers, not only hierarchy labels. Existing editor tools use exact `<roomId>_Floor` lookups, directional doorway prefixes, generated-room container names, exclusion lists, and the literal `starter_floor` spawn lookup. The gameplay scene is also serialized in Unity binary format, which makes a mass-rename diff difficult to inspect.

## Verified Floor Bands

The following bands were read from `SchoolOfTheDead.unity` renderer bounds without saving the scene:

- `B1`: floor top at `Y=-4.800`
- `F1`: floor top at `Y=0.000`
- `F2`: floor top at `Y=4.390` to `Y=4.400`

The 0.01 m variation on `upper_hallway_north` is treated as the same F2 band.

## Naming Standard

Use `ROOM_<floor>_<purpose>[_<location>][_<number>]` for human-facing hierarchy aliases.

- Prefix every room alias with `ROOM_` so rooms group together in the Hierarchy.
- Use `B1`, `F1`, or `F2` from verified world elevation.
- Put purpose before location: `ROOM_F1_Classroom_West_01`.
- Use two-digit numbering where multiple rooms share a purpose.
- Use compass labels only when purpose alone is insufficient.
- Keep gameplay-specific identities such as `Starter`, `Teleporter`, and `Vault` in the readable name.
- Do not rename scripts, classes, floor meshes, transoms, generated children, or gameplay IDs as part of an initial rollout.

## Proposed Mapping

### Basement (`B1`)

| Legacy ID | Proposed hierarchy alias |
|---|---|
| `library_basement` | `ROOM_B1_Library_Main` |
| `library_basement_annex` | `ROOM_B1_Library_Annex` |
| `library_staircase` | `ROOM_B1_Stair_Library` |
| `teleporter_room` | `ROOM_B1_Teleporter` |
| `the_vault` | `ROOM_B1_Vault` |
| `underground_tunnel` | `ROOM_B1_Tunnel_Main` |

### First Floor (`F1`)

| Legacy ID | Proposed hierarchy alias |
|---|---|
| `cafeteria` | `ROOM_F1_Cafeteria` |
| `cafeteria_kitchen` | `ROOM_F1_Kitchen` |
| `cafeteria_west_hallway` | `ROOM_F1_Hallway_CafeteriaWest` |
| `conference_room` | `ROOM_F1_Conference` |
| `courtyard` | `ROOM_F1_Courtyard_Main` |
| `courtyard_east` | `ROOM_F1_Courtyard_East` |
| `courtyard_west` | `ROOM_F1_Courtyard_West` |
| `east_room_3` | `ROOM_F1_Classroom_East_03` |
| `east_stairwell` | `ROOM_F1_Stair_East` |
| `hallway` | `ROOM_F1_Hallway_Central` |
| `history_room` | `ROOM_F1_Classroom_History` |
| `janitors_closet_2` | `ROOM_F1_Janitor_02` |
| `library` | `ROOM_F1_Library` |
| `lower_hallway_east` | `ROOM_F1_Hallway_East` |
| `lower_hallway_south` | `ROOM_F1_Hallway_South` |
| `main_office` | `ROOM_F1_Office_Main` |
| `math_room` | `ROOM_F1_Classroom_Math` |
| `music_room` | `ROOM_F1_Classroom_Music` |
| `parking_lot` | `ROOM_F1_Exterior_ParkingLot` |
| `science_lab` | `ROOM_F1_ScienceLab` |
| `south_end_hallway` | `ROOM_F1_Hallway_SouthEnd` |
| `south_office_classroom_1` | `ROOM_F1_Classroom_South_01` |
| `south_office_classroom_2` | `ROOM_F1_Classroom_South_02` |
| `south_office_classroom_3` | `ROOM_F1_Classroom_South_03` |
| `south_office_classroom_4` | `ROOM_F1_Classroom_South_04` |
| `south_office_hallway` | `ROOM_F1_Hallway_SouthOffice` |
| `staff_entrance_alley` | `ROOM_F1_Exterior_StaffAlley` |
| `staff_entrance_alley_north` | `ROOM_F1_Exterior_StaffAlleyNorth` |
| `stairwell` | `ROOM_F1_Stair_Central_01` |
| `stairwell_2` | `ROOM_F1_Stair_Central_02` |
| `starter` | `ROOM_F1_Classroom_Starter` |
| `west_cafeteria_classroom_1` | `ROOM_F1_Classroom_CafeteriaWest_01` |
| `west_classroom_1` | `ROOM_F1_Classroom_West_01` |
| `west_classroom_2` | `ROOM_F1_Classroom_West_02` |
| `west_south_office` | `ROOM_F1_Office_Southwest` |
| `west_wing_storage` | `ROOM_F1_Storage_WestWing` |

### Second Floor (`F2`)

| Legacy ID | Proposed hierarchy alias |
|---|---|
| `upper_hallway_north` | `ROOM_F2_Hallway_North` |
| `east_room_1` | `ROOM_F2_Classroom_East_01` |
| `east_room_2` | `ROOM_F2_Classroom_East_02` |
| `gym` | `ROOM_F2_Gym` |
| `gym_north_hallway` | `ROOM_F2_Hallway_GymNorth` |
| `janitors_closet` | `ROOM_F2_Janitor_01` |
| `nurses_office` | `ROOM_F2_Nurse` |
| `nurses_office_backroom` | `ROOM_F2_Nurse_Backroom` |
| `principal_office` | `ROOM_F2_Office_Principal` |
| `security_room` | `ROOM_F2_Security` |
| `upper_hallway` | `ROOM_F2_Hallway_CentralSouth` |
| `upper_hallway_2` | `ROOM_F2_Hallway_CentralNorth` |
| `upper_hallway_3` | `ROOM_F2_Hallway_East` |

## Legacy-ID Preservation

The safest first implementation is an additive `RoomIdentity` component on one room root per logical room:

- `legacyId`: immutable current ID, for example `starter`
- `displayName`: readable alias, for example `ROOM_F1_Classroom_Starter`
- `floor`: enum value `B1`, `F1`, or `F2`
- `purpose`: classroom, hallway, office, utility, exterior, and so on
- `schemaVersion`: starts at `1`

Editor tools should continue querying `legacyId` until every reference is migrated. The component should never derive a legacy ID by parsing the GameObject name.

If adding components to the binary scene is undesirable, maintain the same mapping in a versioned `ScriptableObject` registry. That preserves a reviewable source of truth without touching geometry names.

## Known Reference Risks

- `SchoolRoomFurnisher` performs exact `<roomId>_Floor` matching and room-prefixed transom searches.
- `SchoolGameplaySetup` explicitly searches for `starter_floor` when placing the player.
- `SchoolDoorPlacer` parses room IDs from segment names and contains literal exclusions for special rooms and hallway segments.
- `Generated_RoomProps` children currently use legacy room IDs; regeneration would restore those names.
- `Teleporter` documentation and setup conventions refer to `teleporter_room` and `the_vault`.
- Scene references, animation bindings, and third-party scripts may use names not visible to source-code search.
- The binary scene format makes broad hierarchy changes difficult to review in Git.

## Safe Migration Sequence

1. Add a `RoomIdentity` component or registry without renaming anything.
2. Populate all 55 entries using the mapping above and validate duplicate/missing legacy IDs.
3. Update editor tools to resolve rooms by `legacyId`, with current name lookup retained as a compatibility fallback.
4. Add tests for `starter`, doors, generated props, teleporter/vault, and all floor/transom lookups.
5. Rename only logical room parent objects in a dedicated scene commit. Leave mesh children such as `<legacyId>_Floor` unchanged initially.
6. Open the scene in Unity and verify references, doors, spawn placement, generated props, NavMesh, lighting, and multiplayer spawn behavior.
7. Remove compatibility fallbacks only after a full play-mode validation pass.

## Checkpoint 2 Result

- Automatic room renames performed: **none**.
- Scripts/classes renamed: **none**.
- Scene or prefab changes: **none**.
- Legacy IDs preserved: **all 55**.
- Next safe step: implement metadata/registry support before touching hierarchy names.
