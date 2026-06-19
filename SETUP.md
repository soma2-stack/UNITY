# School Of The Dead — Unity Setup Checklist

All C# scripts are already in the repo and will compile automatically.
Work through this list top to bottom once after pulling the branch.

---

## 1. Run the Door Placer

`Tools → School Of The Dead → Place Buyable Doors`

This populates the "Generated_Doors" root with all buyable door cubes.
Re-run it any time you want to regenerate doors.

---

## 2. Bake NavMesh (required for zombies to walk)

1. Select every floor mesh in the scene hierarchy.
2. In the Inspector tick **Navigation Static** (Static flags dropdown).
3. Open **Window → AI → Navigation**.
4. Click the **Bake** tab → **Bake**.

> Stairs and ramps: mark them Navigation Static too so zombies can climb.

---

## 3. Create the Zombie Prefab

1. Drag `Assets/ZombieMale_AAB/Prefabs/URP/ZombieMale_AAB_URP.prefab` into
   the scene.
2. Add these components to the root GameObject:
   - `NavMeshAgent` — Speed ≈ 2, Stopping Distance ≈ 1.5
   - `CapsuleCollider` — Height ≈ 1.8, Radius ≈ 0.35, Center Y ≈ 0.9
   - `ZombieAgent` (Scripts/Enemies/ZombieAgent.cs)
3. Drag it into `Assets/Prefabs/` (create that folder) to make a prefab.
4. Delete the scene instance.

---

## 4. Create Spawn Points

1. Create empty GameObjects named `SpawnPoint_1`, `SpawnPoint_2`, etc.
2. Place them near doorways / corners on the NavMesh surface (ground level).
3. Keep them as children of a `SpawnPoints` empty root for neatness.

---

## 5. Add PlayerHealth to Player

Select your player GameObject and **Add Component → PlayerHealth**.
No configuration needed — defaults are fine for testing.

---

## 6. Create the GameManager Object

Create an empty GameObject named `GameManager` in the scene root. Add:

| Component | Script file | Key fields to set |
|---|---|---|
| `PlayerPoints` | Systems/PlayerPoints.cs | *(singleton, no config)* |
| `PointsHud` | UI/PointsHud.cs | *(now a no-op; superseded by GameHud)* |
| `ZombieSpawner` | Enemies/ZombieSpawner.cs | Zombie Prefab → your prefab; Spawn Points → array of SpawnPoint GameObjects |
| `RoundManager` | Systems/RoundManager.cs | *(auto-links to ZombieSpawner on same object)* |
| `GameHud` | UI/GameHud.cs | *(auto-links everything; no manual wiring needed)* |

---

## 7. Set Up WeaponController on the Player

1. Select your player (or the Camera child).
2. **Add Component → WeaponController**.
3. In the Weapons list add entries for each gun:
   - Set Name, Damage, Fire Rate, Magazine Size, Reserve Ammo, etc.
   - Weapon Model → drag the matching mesh from
     `Assets/Low Poly Weapons VOL.1/` (or leave null for hitscan-only).
4. Camera must be tagged **MainCamera** (WeaponController uses `Camera.main`).

---

## 8. Set Up the Teleporter

### In teleporter_room:
1. Create an empty GameObject `TeleporterPad` near the floor.
2. Add component `Teleporter` (Scripts/Interactables/Teleporter.cs).
3. Set **Destination** → `VaultArrivalPoint` (see next step).
4. Optionally set **Cost** (0 = free).

### In the_vault:
1. Create an empty GameObject `VaultArrivalPoint` on the floor.
2. *(Optional)* Create a second `Teleporter` on a pad in the vault pointing
   back to a `TeleporterReturnPoint` in teleporter_room.

---

## 9. Assign the Teleporter Destination

Go back to `TeleporterPad` and drag `VaultArrivalPoint` into the
**Destination** field.

---

## 10. Verify the Console

Open `Window → General → Console`. There should be **zero red errors**.
Yellow warnings are fine. Common red errors and fixes:

| Error | Fix |
|---|---|
| `NavMeshAgent on disabled object` | Enable the zombie GameObject before calling NavMesh methods |
| `NullReferenceException in ZombieAgent` | Make sure a player with CharacterController exists in the scene |
| `FindFirstObjectByType returns null` | Make sure PlayerPoints/RoundManager GameObjects are in the scene |

---

## 11. Test Run

Hit Play and verify:

- [ ] Player moves with WASD, sprints with Shift, crouches with Ctrl
- [ ] Left-click fires the current weapon; R reloads
- [ ] 1-9 / scroll wheel switches weapons
- [ ] HUD shows weapon name, ammo, round number, zombie count, P1 points
- [ ] Pressing E on a door (with enough points) slides it down
- [ ] Zombie spawns at round start and walks toward the player
- [ ] Killing a zombie awards 100 points (shown in HUD)
- [ ] Step into teleporter pad → teleports to the vault

---

## Folder Reference

```
Assets/
  Editor/
    SchoolDoorPlacer.cs       ← Tools → School Of The Dead → Place Buyable Doors
    SchoolRoomTextureApplier.cs
  Materials/SchoolOfTheDead/  ← all generated materials (floors, walls, doors, etc.)
  Prefabs/                    ← put your Zombie prefab here
  Scripts/
    Enemies/   ZombieAgent.cs, ZombieSpawner.cs
    Interactables/ Door.cs, Teleporter.cs
    Player/    PlayerMovement.cs, PlayerHealth.cs, CoDCamera.cs, CoDMovement.cs
    Systems/   PlayerPoints.cs, RoundManager.cs
    UI/        GameHud.cs, PointsHud.cs, MainMenuManager.cs
    Weapons/   Weapon.cs, WeaponController.cs
  Scenes/
    SchoolOfTheDead.unity
```
