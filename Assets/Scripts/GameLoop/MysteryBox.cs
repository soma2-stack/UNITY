using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The MYSTERY BOX (Call of Duty Zombies style). Press E (costs 950 points) to get
/// a RANDOM weapon from a built-in pool, handed over via
/// <see cref="WeaponController.GiveWeapon"/>.
///
/// Requires the map power to be on (<see cref="PowerState.IsOn"/>) — gated here, not
/// in any shared system. Null-safe: needs a WeaponController in the scene to grant.
/// </summary>
public class MysteryBox : InteractableBase
{
    [Header("Mystery Box")]
    [Tooltip("Cost per spin.")]
    public int cost = 950;
    [Tooltip("Require the map power to be on before the box can be used.")]
    public bool requirePower = true;

    // Drag your low poly gun model prefabs into the Weapon Model slot of each entry
    // here. The weaponModel field on each Weapon entry is what shows in the player's
    // hands when that weapon is equipped.
    [Header("Weapon Pool")]
    [Tooltip("Weapons available from the Mystery Box. Assign weapon stats and the in-hand model for each entry. If left empty, a default pool is used as fallback.")]
    public List<Weapon> weaponPool = new List<Weapon>();

    [Header("Teddy Bear")]
    [Tooltip("Chance (0..1) a spin gives a Teddy Bear (no weapon) and relocates the box.")]
    public float teddyBearChance = 0.1f;
    [Tooltip("Possible world positions the box can relocate to on a Teddy Bear. Needs 2+ entries to actually move.")]
    public Transform[] boxLocations;

    // Pre-fill the inspector pool with the default weapons when the component is first
    // added (or Reset in the inspector) so a designer only needs to drag in the models.
    private void Reset()
    {
        weaponPool = BuildPool();
    }

    // Built-in weapon pool (weaponModel left null is fine; ammo is initialised on grant).
    private static List<Weapon> BuildPool()
    {
        return new List<Weapon>
        {
            new Weapon { weaponName = "SMG",        damage = 30,  fireRate = 12f, automatic = true,  range = 80f,  spread = 2.5f, magazineSize = 30, reserveAmmo = 240, reloadTime = 1.8f },
            new Weapon { weaponName = "Shotgun",    damage = 90,  fireRate = 1.5f, automatic = false, range = 25f,  spread = 6f,   magazineSize = 6,  reserveAmmo = 48,  reloadTime = 2.6f },
            new Weapon { weaponName = "Assault Rifle", damage = 45, fireRate = 9f, automatic = true,  range = 120f, spread = 1.5f, magazineSize = 30, reserveAmmo = 300, reloadTime = 2.2f },
            new Weapon { weaponName = "LMG",        damage = 55,  fireRate = 10f, automatic = true,  range = 120f, spread = 2f,   magazineSize = 75, reserveAmmo = 300, reloadTime = 4.0f },
            new Weapon { weaponName = "Sniper",     damage = 200, fireRate = 1f,  automatic = false, range = 300f, spread = 0f,   magazineSize = 5,  reserveAmmo = 50,  reloadTime = 3.0f },
            new Weapon { weaponName = "Magnum",     damage = 80,  fireRate = 3f,  automatic = false, range = 90f,  spread = 1f,   magazineSize = 6,  reserveAmmo = 60,  reloadTime = 2.0f },
        };
    }

    protected override string GetPromptText()
    {
        if (requirePower && !PowerState.IsOn)
        {
            return "Mystery Box   (turn on power)";
        }
        return "Press E   Mystery Box   [" + cost + "]";
    }

    protected override void OnInteract()
    {
        if (NetworkGameplayCoordinator.IsNetworkActive)
        {
            NetworkGameplayCoordinator.RequestMysteryBox(this);
            return;
        }

        if (requirePower && !PowerState.IsOn)
        {
            Debug.Log("[MysteryBox] Power is off.");
            return;
        }

        WeaponController wc = LocalPlayer.Weapon;
        if (wc == null)
        {
            Debug.LogWarning("[MysteryBox] No WeaponController in scene; cannot grant a weapon.");
            return;
        }

        // Charge first - in CoD the spin costs points even when it gives a Teddy Bear.
        if (!TryCharge(cost))
        {
            return;
        }

        bool teddy = RollTeddy();
        int weaponIndex = teddy ? -1 : RollWeaponIndex();
        if (teddy)
        {
            Debug.Log("[MysteryBox] Teddy Bear! Box is moving.");
            RelocateBox();
            return; // no weapon - the player still paid, exactly like CoD
        }

        ApplyMysteryResult(weaponIndex);
    }

    public bool RollTeddy()
    {
        return Random.value < Mathf.Clamp01(teddyBearChance);
    }

    public int RollWeaponIndex()
    {
        List<Weapon> pool = (weaponPool != null && weaponPool.Count > 0) ? weaponPool : BuildPool();
        return pool.Count > 0 ? Random.Range(0, pool.Count) : -1;
    }

    /// <summary>Name of the weapon at a rolled pool index (null for teddy / empty pool).
    /// Used by the server to record wall-buy ownership when the box grants a weapon.</summary>
    public string WeaponNameAt(int weaponIndex)
    {
        if (weaponIndex < 0)
        {
            return null;
        }
        List<Weapon> pool = (weaponPool != null && weaponPool.Count > 0) ? weaponPool : BuildPool();
        return pool.Count > 0 ? pool[Mathf.Clamp(weaponIndex, 0, pool.Count - 1)].weaponName : null;
    }

    public void ApplyMysteryResult(int weaponIndex)
    {
        if (weaponIndex < 0)
        {
            Debug.Log("[MysteryBox] Teddy Bear! Box is moving.");
            return;
        }

        WeaponController wc = LocalPlayer.Weapon;
        if (wc == null)
        {
            Debug.LogWarning("[MysteryBox] No WeaponController in scene; cannot grant a weapon.");
            return;
        }

        List<Weapon> pool = (weaponPool != null && weaponPool.Count > 0) ? weaponPool : BuildPool();
        if (pool.Count == 0)
        {
            return;
        }

        // Give a FRESH COPY, never the shared pool object, so the player firing / reloading
        // (or a later Pack-a-Punch) can't mutate the template and taint future rolls.
        Weapon prize = pool[Mathf.Clamp(weaponIndex, 0, pool.Count - 1)].Clone();
        wc.GiveWeapon(prize);
        Debug.Log("[MysteryBox] Granted: " + prize.weaponName);
    }

    /// <summary>
    /// Teleports the box to a random configured location that isn't its current spot.
    /// Needs 2+ entries in <see cref="boxLocations"/>; otherwise it stays put and warns.
    /// </summary>
    public void RelocateBox()
    {
        if (boxLocations == null || boxLocations.Length < 2)
        {
            Debug.LogWarning("[MysteryBox] No alternate box locations configured; box stays in place.");
            return;
        }

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Transform dest = boxLocations[Random.Range(0, boxLocations.Length)];
            if (dest != null && (dest.position - transform.position).sqrMagnitude > 0.01f)
            {
                transform.position = dest.position;
                transform.rotation = dest.rotation;
                return;
            }
        }
    }

    public void ApplyNetworkTransform(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
    }
}
