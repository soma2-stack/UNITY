using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click weapon loadout setup for "School Of The Dead".
///
/// Fills the Player prefab's WeaponController with real first-person gun models from
/// the "Low Poly Weapons VOL.1" pack. For each gun in the starting loadout it:
///   - instantiates the weapon prefab under a "WeaponHolder" child of the Player Camera
///     (so the model renders on screen, COD-style),
///   - builds a <see cref="Weapon"/> data object with stats chosen by gun category,
///   - assigns that instance as the weapon's model and seeds its ammo,
///   - adds it to WeaponController.weapons (only the first weapon is left enabled).
///
/// The category stat presets are exposed via <see cref="MakeWeapon"/> and
/// <see cref="GetGunCatalog"/> so the Mystery Box / wall buys can reuse them later.
///
/// Re-runnable: removes any WeaponHolder it previously created and clears the
/// WeaponController's weapon list before rebuilding.
/// </summary>
public static class WeaponLoadoutSetup
{
    private const string PrefabPath = "Assets/Prefabs/Player.prefab";
    private const string WeaponPackPrefabFolder = "Assets/Low Poly Weapons VOL.1/Prefabs";
    private const string CameraChildName = "Player Camera";
    private const string WeaponHolderName = "WeaponHolder";

    // First-person offset so the muzzle sits low-right of screen centre. Tweak to taste.
    private static readonly Vector3 WeaponHolderLocalPosition = new Vector3(0.25f, -0.25f, 0.5f);

    /// <summary>Broad gun families used to pick stat presets.</summary>
    public enum GunCategory
    {
        Pistol,
        SMG,
        Rifle,
        Shotgun,
        Sniper,
        LMG,
        Rocket
    }

    /// <summary>A catalog entry: which pack prefab maps to which stat category.</summary>
    public struct GunEntry
    {
        public string displayName;   // In-game weapon name
        public string prefabName;    // File name (no extension) under the weapon pack
        public GunCategory category;

        public GunEntry(string displayName, string prefabName, GunCategory category)
        {
            this.displayName = displayName;
            this.prefabName = prefabName;
            this.category = category;
        }
    }

    // The guns the player starts with (the rest of the catalog is Mystery-Box fodder).
    private static readonly GunEntry[] StartingLoadout =
    {
        new GunEntry("M1911", "M1911", GunCategory.Pistol),
        new GunEntry("AK74", "AK74", GunCategory.Rifle),
        new GunEntry("Benelli M4", "Bennelli_M4", GunCategory.Shotgun),
        new GunEntry("M107", "M107", GunCategory.Sniper),
    };

    [MenuItem("Tools/School Of The Dead/Set Up Weapons")]
    public static void SetUpWeapons()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Debug.LogError("[WeaponLoadoutSetup] Player prefab not found at " + PrefabPath +
                           ". Run Tools/School Of The Dead/Set Up Player first, then re-run this.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            WeaponController controller = root.GetComponent<WeaponController>();
            if (controller == null)
            {
                Debug.LogError("[WeaponLoadoutSetup] No WeaponController on the Player prefab root. " +
                               "Re-run Set Up Player, then this tool.");
                return;
            }

            Transform cameraTransform = FindChildByName(root.transform, CameraChildName);
            if (cameraTransform == null)
            {
                Debug.LogError("[WeaponLoadoutSetup] Could not find a '" + CameraChildName +
                               "' child under the Player prefab. Re-run Set Up Player, then this tool.");
                return;
            }

            // --- Re-runnable: tear down anything a previous run added. ---
            Transform existingHolder = FindChildByName(cameraTransform, WeaponHolderName);
            if (existingHolder != null)
            {
                Object.DestroyImmediate(existingHolder.gameObject);
            }
            if (controller.weapons == null)
            {
                controller.weapons = new List<Weapon>();
            }
            controller.weapons.Clear();

            // --- WeaponHolder: parents all in-hand models at a first-person offset. ---
            GameObject holder = new GameObject(WeaponHolderName);
            holder.transform.SetParent(cameraTransform, false);
            holder.transform.localPosition = WeaponHolderLocalPosition;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one;

            // Point the WeaponController's aim ray at the camera that holds the guns.
            controller.aimCamera = cameraTransform;

            // --- Build the loadout. ---
            var summary = new System.Text.StringBuilder();
            int added = 0;
            foreach (GunEntry entry in StartingLoadout)
            {
                string gunPrefabPath = WeaponPackPrefabFolder + "/" + entry.prefabName + ".prefab";
                GameObject gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gunPrefabPath);
                if (gunPrefab == null)
                {
                    Debug.LogWarning("[WeaponLoadoutSetup] Missing weapon prefab: " + gunPrefabPath +
                                     " - skipping " + entry.displayName + ".");
                    continue;
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(gunPrefab);
                instance.name = entry.displayName;
                instance.transform.SetParent(holder.transform, false);
                instance.transform.localPosition = Vector3.zero;   // Tweak per-gun in the prefab if needed.
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                // Only the first weapon stays visible; WeaponController re-equips at runtime.
                instance.SetActive(added == 0);

                Weapon weapon = MakeWeapon(entry.displayName, entry.category, instance);
                weapon.InitAmmo();
                controller.weapons.Add(weapon);

                summary.Append("\n  - ").Append(entry.displayName)
                       .Append(" [").Append(entry.category).Append("]  dmg ").Append(weapon.damage)
                       .Append(", mag ").Append(weapon.magazineSize)
                       .Append(", reserve ").Append(weapon.reserveAmmo)
                       .Append(", auto ").Append(weapon.automatic);
                added++;
            }

            controller.currentIndex = 0;

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

            if (added == 0)
            {
                Debug.LogWarning("[WeaponLoadoutSetup] No weapon prefabs were found under " +
                                 WeaponPackPrefabFolder + ". Loadout is empty.");
            }
            else
            {
                Debug.Log("[WeaponLoadoutSetup] Wired " + added + " weapon(s) onto " + PrefabPath +
                          ". WeaponHolder at " + WeaponHolderLocalPosition + " under '" + CameraChildName +
                          "'. Loadout:" + summary +
                          "\nEquip the player in a scene to test (1-" + added +
                          " / scroll to switch, LMB fire, R reload).");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// Build a <see cref="Weapon"/> for a category using the shared COD-zombies presets.
    /// Reusable by the Mystery Box / wall buys: pass the in-hand model instance (or null).
    /// </summary>
    public static Weapon MakeWeapon(string weaponName, GunCategory category, GameObject model)
    {
        Weapon w = new Weapon { weaponName = weaponName, weaponModel = model };

        switch (category)
        {
            case GunCategory.Pistol:
                w.damage = 40; w.fireRate = 4f; w.automatic = false;
                w.magazineSize = 8; w.reserveAmmo = 80; w.reloadTime = 1.3f;
                w.range = 60f; w.spread = 1f;
                break;

            case GunCategory.SMG:
                w.damage = 25; w.fireRate = 12f; w.automatic = true;
                w.magazineSize = 32; w.reserveAmmo = 240; w.reloadTime = 1.8f;
                w.range = 70f; w.spread = 3f;
                break;

            case GunCategory.Rifle:
                w.damage = 45; w.fireRate = 9f; w.automatic = true;
                w.magazineSize = 30; w.reserveAmmo = 240; w.reloadTime = 2.2f;
                w.range = 120f; w.spread = 2f;
                break;

            case GunCategory.Shotgun:
                w.damage = 120; w.fireRate = 1.5f; w.automatic = false;
                w.magazineSize = 7; w.reserveAmmo = 56; w.reloadTime = 2.6f;
                w.range = 25f; w.spread = 6f;
                break;

            case GunCategory.Sniper:
                w.damage = 300; w.fireRate = 0.8f; w.automatic = false;
                w.magazineSize = 5; w.reserveAmmo = 40; w.reloadTime = 3.0f;
                w.range = 300f; w.spread = 0f;
                break;

            case GunCategory.LMG:
                w.damage = 50; w.fireRate = 11f; w.automatic = true;
                w.magazineSize = 100; w.reserveAmmo = 400; w.reloadTime = 4.0f;
                w.range = 130f; w.spread = 3f;
                break;

            case GunCategory.Rocket:
                w.damage = 500; w.fireRate = 0.5f; w.automatic = false;
                w.magazineSize = 1; w.reserveAmmo = 8; w.reloadTime = 3.5f;
                w.range = 150f; w.spread = 1f;
                break;
        }

        return w;
    }

    /// <summary>
    /// Full catalog of usable guns in the pack (scopes / grenades / lasers excluded),
    /// each mapped to its stat category. The Mystery Box can roll from this list and call
    /// <see cref="MakeWeapon"/> to spawn a configured weapon.
    /// </summary>
    public static GunEntry[] GetGunCatalog()
    {
        return new[]
        {
            new GunEntry("M1911", "M1911", GunCategory.Pistol),
            new GunEntry("Uzi", "Uzi", GunCategory.SMG),
            new GunEntry("AK74", "AK74", GunCategory.Rifle),
            new GunEntry("M4", "M4_8", GunCategory.Rifle),
            new GunEntry("Benelli M4", "Bennelli_M4", GunCategory.Shotgun),
            new GunEntry("M107", "M107", GunCategory.Sniper),
            new GunEntry("M249", "M249", GunCategory.LMG),
            new GunEntry("M2 .50cal", "M2_50cal", GunCategory.LMG),
            new GunEntry("RPG-7", "RPG7", GunCategory.Rocket),
        };
    }

    // Depth-first search for a direct/descendant child with the given name.
    private static Transform FindChildByName(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name)
            {
                return child;
            }
            Transform found = FindChildByName(child, name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }
}
