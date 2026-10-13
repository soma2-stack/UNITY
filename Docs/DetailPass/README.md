# School of the Dead — Environment Detail Pass (2026-10-12)

The existing map was dressed by adding props. **No original architecture, room, doorway, stair or gameplay object was moved, edited or deleted.** Everything new lives under one scene root, `SOTD_DetailPass` (Floor1_Main / Floor2_Upper / Basement / Exterior → one group per room). Disable that object to see the original map. Delete it to remove the pass completely.

## Deliverables

| Item | Location |
|---|---|
| Updated Unity scene (same name, so the game's scene loading still works) | `Assets/Scenes/SchoolOfTheDead.unity` |
| Verified backup of the original scene, NavMesh and lighting-preview scene (sha256-matched) | `Backups/2026-10-12_PreDetail/` |
| New props modelled in Blender (59 FBX → prefabs) + materials + poster prefabs | `Assets/SchoolOfTheDead_Detail/{Models,Prefabs,Materials}` |
| Blender working file: locked reference architecture, locked existing props, all placed detail organised by floor/room, plus the asset-lab scene with the 59 source models | `BlenderWork/SOTD_DetailWorkspace.blend` (local, not in git) |
| Re-runnable editor tooling | `Assets/Editor/SchoolDetailPass/*.cs` |
| Cosmetic flicker component for failing lights | `Assets/Scripts/Environment/SOTDFlickerLight.cs` |
| Asset inventory (217 assets: size, tris, materials, colliders, scene usage, suggested rooms) | `Docs/DetailPass/AssetInventory.md` |
| Before / after / QA / play-mode screenshots, Blender plan render | `Captures/DetailPass/` |
| Build logs, NavMesh baseline vs after, automated QA | `Docs/DetailPass/*.txt` |

## Workflow used

1. **Unity inspection and backup.** Unity 6000.4.10f1, URP 17.4 (Forward+), NGO multiplayer. The map is `SchoolOfTheDead.unity`: 58 room nodes under `School Map - Zombies Scale` on three levels (basement at −4.8 m, ground at 0 m, upper at +4.4 m), plus `Generated_*` gameplay roots. The scene and NavMesh were copied and checksum-verified before any change.
2. **Unity → Blender.** The FBX Exporter package isn't installed, and I didn't want to change the project config, so `SchoolGeometryExporter` writes world-space OBJ in Blender axes (x=Ux, y=Uz, z=Uy). That covers architecture (1,240 objects / 177,207 tris) and existing props (5,072 objects). Both were imported and checked 1:1 (math-room floor and stair step bounds match exactly; triangle counts match). They are organised into `Ref_Floor1/Floor2/Basement/Stairs/Exterior → room` collections and locked (`hide_select`).
3. **Inventory.** `SchoolAssetInventory` measured every prop prefab and model you own. Placement reuses existing prefabs directly (school pack, lockers, Meshy models, kitchen set, gym set, fence, cars, bushes), so their materials and colliders are kept.
4. **Blender detailing.** 59 supplemental props were modelled procedurally in Blender to fill gaps the pack doesn't cover: light panels, industrial lamps, pipe runs, electrical panels, metal shelving (stocked and empty), boxes, vending machine, water fountain, extinguisher, cots, mattress, bleachers, hoop, piano, music stands, easels, cubbies, whiteboard, bulletin board, projector screen, counters, conference table, workbench, ladder, boilers, generator, sandbags, jersey barriers, sawhorses, cones, street lamps, benches, picnic tables, dumpster, pallets, drums, fallen ceiling tiles, blood decals, etc. They were exported as FBX (axis-verified with a test cube), imported into Unity with material remapping onto your existing `Furniture - *` materials, and saved as prefabs.
5. **Placement in Unity** (`SchoolDetailKit`). Room descriptors come from the actual geometry: wall faces are raycast-fitted, doorways are detected by wall sampling, and heights come from floor and ceiling renderers. Every prop is validated before it is kept. It must be grounded by its bounds, stay inside the room, keep a 2 m doorway clearance, and avoid interactables, perk machines, doors, teleporters, books, power-ups and zombie spawns. It is also rejected if it overlaps an existing collider, an existing prop or another new prop. Rejected placements are discarded and counted per room. Layouts are seeded per room, so rebuilds are deterministic.
6. **Blender round trip.** The final placed detail was exported back to the `.blend` (`SOTD_Detail_Placed (from Unity)`). Object and triangle counts match Unity per floor.

## Visual design

- **Floor 1**: warm fluorescent panels (some dead, about 18% flicker), lockers, notice boards, fountains and extinguishers in the halls.
- **Floor 2**: colder green-white light, more broken fixtures and more flicker.
- **Basement**: amber industrial pendant lamps, pipe runs, red emergency glow. Darker, but readable.
- Exit signs with a red glow over stair and outdoor doorways. 22 room plaques with TextMeshPro labels (e.g. `114 MATH`, `NURSE`).
- Your own poster art (`Textures/SchoolOfTheDead/Posters`) became 5 wall-poster prefabs.
- Outbreak storytelling: knocked-over desks and chairs, desk barricades shoved into corners, blood pools and wall smears, fallen ceiling tiles, survivor camps (mattress, backpack, supplies) in storage rooms and the nurse backroom, and an evacuation checkpoint (barriers, sandbags, cones) at both front entrances.
- 247 real-time point lights (no shadows), 58 with flicker. 61 unique materials, all instancing-enabled.

## Room-by-room checklist

Every room below was built and then inspected in a screenshot after its final build, unless marked otherwise.

| Level | Room | Result | New props | Lights |
|---|---|---|---|---|
| F1 | Starter Classroom (101) | Already dressed: lights + wall decor only | 30 | 6 |
| F1 | Science Lab | Already dressed: lights + decor | 19 | 4 |
| F1 | English Classroom (`library`) | Already dressed: lights + decor | 21 | 5 |
| F1 | Cafeteria | Already dressed: lights + decor | 26 | 6 |
| F1 | Cafeteria Kitchen | Already dressed: lights + decor | 12 | 3 |
| F1 | West Corridor Chemistry Lab | Already dressed: lights + decor | 21 | 5 |
| F1 | West Wing Biology / English | Already dressed: lights + decor | 10 / 9 | 3 / 3 |
| F1 | Main Hallway | Lockers, boards, fountains, litter, exit sign | 41 | 4 |
| F1 | West Cafeteria Corridor | Wall decor (has lockers already), litter | 38 | 6 |
| F1 | South Wing Hallway | Lockers, fixtures, plaques | 72 | 4 |
| F1 | Lower East Wing Corridor | Lockers, fixtures, plaques | 143 | 6 |
| F1 | Office South Hallway | Lockers, fixtures, plaques | 221 | 6 |
| F1 | South End Hallway | Lockers, fixtures | 125 | 6 |
| F1 | History Room (112) | Chalkboard classroom, globe, barricade corner | 93 | 6 |
| F1 | Math Room (114) | Two whiteboards, desk grid | 83 | 5 |
| F1 | Music Room (116 Choir) | Chair arcs, stands, piano | 51 | 2 |
| F1 | Library (`east_room_3`) | Book stacks, reading tables, circulation desk | 124 | 6 |
| F1 | Main Office | Reception counter, waiting benches, desks, files | 68 | 6 |
| F1 | Conference Room | 3 conference tables, screen | 51 | 5 |
| F1 | South Office Biology Lab (121) | Lab benches + stools, storage zone | 109 | 6 |
| F1 | South Office Music/Band (123) | Chair arcs, piano zone | 119 | 6 |
| F1 | South Office Art Studio (125) | Craft tables, easels, cubbies | 171 | 6 |
| F1 | South Office Computer Lab (127) | Monitor rows, dumped monitors, carts | 182 | 6 |
| F1 | Janitor's Closet B | Shelving, panels, mop bucket | 19 | 2 |
| F1 | West South Office (Counselor) | Desks, files, storage | 62 | 5 |
| F1 | West Wing Storage | Shelving islands, crates, survivor camp | 37 | 4 |
| F1/F2 | Stairwell, Secondary Stairwell, East Stairwell | Exit signs + one light each (stairs kept clear) | 3–5 | 1 |
| F2 | Gymnasium | Already dressed: hoops, lights, decor | 37 | 6 |
| F2 | 2nd Floor Hallway / Hallway 2 / North Connector / East Wing Corridor / Gym North Connector | Lockers, fixtures, plaques | 10 / 14 / 99 / 144 / 47 | 1–6 |
| F2 | Principal's Office | Desk, trophy wall, meeting table, secretary pool | 121 | 6 |
| F2 | Security & Breaker Room | Already dressed: lights + decor | 14 | 4 |
| F2 | Nurse's Office | Cots, medicine cabinet, desk | 25 | 3 |
| F2 | Nurse's Backroom | Supplies, survivor mattress | 25 | 1 |
| F2 | Janitor's Closet A (Maintenance) | Workbench, shelving islands, generator | 42 | 3 |
| F2 | East Wing Classroom A (204 English) | Desk grid, bookshelves | 102 | 6 |
| F2 | East Wing Classroom B (206) | Desk grid | 71 | 4 |
| B | Utility Tunnel | Pipe runs, panels, crates | 20 | 3 |
| B | Fallout Shelter Vault | Supply shelves, cots, generator, sandbags | 67 | 6 |
| B | Library Basement / Annex | Stocked shelving, boilers, pipes | 53 / 45 | 6 / 6 |
| B | Teleporter Room | Boilers, generators, panels, pipes | 95 | 6 |
| B | Secret Stairwell | Exit sign + light | 3 | 1 |
| EX | Parking Lot | Street lamps, abandoned cars, entrance checkpoints, benches, bike rack, boundary fence, debris | 84 | 4 |
| EX | Courtyards (center / east / west) | Benches around the fountain, picnic tables, litter | 13 / 10 / 7 | – |
| EX | Staff Entrance Alleys (2) | Dumpsters, pallets, drums, lamp | 18 / 17 | 1 / 1 |

## Verification

- **Automated QA** (`Docs/DetailPass/qa_auto.txt`): 3,174 placed props checked. 0 floating, 0 sunk below the floor, 0 broken or non-URP materials.
- **Navigation**: the NavMesh was rebaked with doors excluded, as before. Seating, clutter and wall decor are excluded from the bake (`NavMeshModifier`), so desk grids don't fragment zombie paths. Reachability was grid-tested from the player start over about 5,300 floor samples. Every room that was reachable before the pass is still fully reachable (`nav_baseline.txt` vs `nav_after_grid.txt`). All 5 zombie spawn points are on the mesh with complete paths.
- **Play mode**: started from the main menu via `StartSolo()`. The player spawned, round 1 started, zombies spawned and pathed, and the console showed no errors from this pass. The only warnings were a video timestamp note and a URP shadow-atlas note.

## Remaining issues / not done

1. **Basement and stairs are off the NavMesh.** This was already true of the original bake: the utility tunnel, vault, library basement/annex, teleporter room and the stair geometry have no path from the player start. It looks intentional (teleporter / secret stair access), but if zombies should reach the basement, the original stair or step geometry needs attention. I didn't change the architecture.
2. **The game view is brighter than the editor captures.** The player camera's post-processing or exposure makes rooms read washed out in play mode, both with and without the pass (`Captures/DetailPass/PlayMode/*detailOFF*`). The new fixture lights were reduced by 28% to compensate. Tune further with one multiplier if needed.
3. **The gym, security room, cafeteria, kitchen, starter room, science lab, English room and west-wing rooms were already furnished.** They only got lighting, wall dressing and light clutter, to avoid overwriting your layout.
4. **Not done:** no light baking (scene uses real-time lights, `bakedGI` on with no lightmaps), no occlusion-culling bake, and no co-op (2-client) playtest.
5. **Unity reserialized a few third-party materials when it loaded them** (`filing-cabinet.mat`, `ARCADE … AFRC_*.mat`). They were left out of the commit.

## Rebuilding / tuning

All from the Unity editor (for example the MCP `execute_code` tool, or a menu item if you add one):

```csharp
SchoolDetailKit.BuildAllInteriors();   // every room in the plan, deterministic
SchoolDetailKit.BuildDoorSigns();
SchoolDetailKit.BuildExterior();
SchoolDetailKit.BakeNavMesh();
SchoolDetailKit.QAReport("Docs/DetailPass/qa_auto.txt");
SchoolDetailKit.BuildRooms("math_room,gym");  // just some rooms
```

Room → layout mapping is the `Plan` table in `SchoolDetailDirector.cs`. Light colour and intensity per floor is in `StyleFor()`. To regenerate the Blender assets, open the `.blend`, switch to the `AssetLab` scene and run the `sotd_builder.py` and `sotd_assets.py` text blocks.
