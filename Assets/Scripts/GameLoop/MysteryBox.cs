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
    public const float RevealDuration = 2f;
    private const float OwnerClaimDuration = 6f;
    private const float PublicClaimDuration = 4f;
    // Teddy Bear: after the 2s roll, hold a clear "BOX MOVING" result this long before the
    // server relocates the box (kept in sync with the reveal HUD's teddy result phase).
    public const float TeddyResultDuration = 1.5f;
    // pendingWeaponIndex sentinel meaning "a Teddy Bear is resolving" (no weapon, box will move).
    // -1 = nothing pending, >=0 = a claimable weapon prize, -2 = Teddy resolving.
    private const int TeddyPendingIndex = -2;

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

    private int pendingWeaponIndex = -1;
    private ulong pendingBuyerClientId;
    private double pendingRevealEndsAt;
    private double pendingOwnerEndsAt;
    private double pendingExpiresAt;
    private bool localPlayerIsPendingBuyer;

    public bool HasPendingPrize => pendingWeaponIndex >= 0;
    // A Teddy Bear result is playing out (roll + "BOX MOVING") before the box relocates.
    public bool IsTeddyPending => pendingWeaponIndex == TeddyPendingIndex;
    // Either a weapon prize or a Teddy is resolving; used to block new spins while busy.
    public bool IsResolvingPrize => pendingWeaponIndex >= 0 || IsTeddyPending;
    public int PendingWeaponIndex => pendingWeaponIndex;
    public ulong PendingBuyerClientId => pendingBuyerClientId;
    public double PendingRevealEndsAt => pendingRevealEndsAt;
    public double PendingOwnerEndsAt => pendingOwnerEndsAt;
    public double PendingExpiresAt => pendingExpiresAt;

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
        if (IsTeddyPending)
        {
            double teddyNow = NetworkGameplayCoordinator.SharedTime;
            return teddyNow < pendingRevealEndsAt ? "Mystery Box is rolling..." : "Teddy Bear! Box is moving...";
        }

        if (HasPendingPrize)
        {
            double now = NetworkGameplayCoordinator.SharedTime;
            if (now < pendingRevealEndsAt)
            {
                return "Mystery Box is rolling...";
            }
            if (now >= pendingExpiresAt)
            {
                return "Mystery Box prize expiring...";
            }

            string prizeName = GetPendingPrizeName();
            if (now < pendingOwnerEndsAt && !localPlayerIsPendingBuyer)
            {
                int seconds = Mathf.Max(1, Mathf.CeilToInt((float)(pendingOwnerEndsAt - now)));
                return "Mystery Box reserved [" + seconds + "]";
            }
            return "Press E   Take " + prizeName;
        }

        if (requirePower && !PowerState.IsOn)
        {
            return "Mystery Box   (turn on power)";
        }
        return "Press E   Mystery Box   [" + cost + "]";
    }

    protected override void OnInteract()
    {
        if (IsTeddyPending)
        {
            return; // box is resolving a Teddy Bear — no spin, no claim, until it moves
        }

        if (HasPendingPrize)
        {
            if (!CanLocalPlayerClaimPrize())
            {
                return;
            }

            if (NetworkGameplayCoordinator.IsNetworkActive)
            {
                NetworkGameplayCoordinator.RequestMysteryBoxClaim(this);
            }
            else if (TryClaimPendingPrize(0, out int claimedWeaponIndex, out _))
            {
                ApplyMysteryResult(claimedWeaponIndex);
            }
            return;
        }

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
            // Play the same 2s roll + a clear "BOX MOVING" result, THEN relocate on expiry
            // (handled in Update). The player still paid, exactly like CoD.
            Debug.Log("[MysteryBox] Teddy Bear! Box will move after the reveal.");
            BeginPendingTeddy(0);
            return;
        }

        BeginPendingPrize(weaponIndex, 0, true);
    }

    protected override void Update()
    {
        base.Update();

        if (!IsResolvingPrize || NetworkGameplayCoordinator.SharedTime < pendingExpiresAt)
        {
            return;
        }

        bool teddy = IsTeddyPending;
        if (NetworkGameplayCoordinator.IsNetworkActive)
        {
            // Server-authoritative: only the server relocates / clears the shared state.
            if (NetworkGameplayCoordinator.IsServer)
            {
                if (teddy)
                {
                    NetworkGameplayCoordinator.RelocateMysteryBoxAndClear(this);
                }
                else
                {
                    NetworkGameplayCoordinator.ExpireMysteryBoxPrize(this);
                }
            }
        }
        else
        {
            // Solo: relocate locally once the Teddy result finishes, then clear.
            if (teddy)
            {
                RelocateBox();
            }
            ClearPendingPrize();
        }
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

        // The prize is granted only when the pending claim is accepted.
    }

    public void BeginPendingPrize(int weaponIndex, ulong buyerClientId, bool showRevealForLocalPlayer)
    {
        double now = NetworkGameplayCoordinator.SharedTime;
        ApplyPendingPrizeState(
            weaponIndex,
            buyerClientId,
            now + RevealDuration,
            now + RevealDuration + OwnerClaimDuration,
            now + RevealDuration + OwnerClaimDuration + PublicClaimDuration,
            showRevealForLocalPlayer);
    }

    public void ApplyPendingPrizeState(
        int weaponIndex,
        ulong buyerClientId,
        double revealEndsAt,
        double ownerEndsAt,
        double expiresAt,
        bool showRevealForLocalPlayer)
    {
        bool changed = pendingWeaponIndex != weaponIndex || pendingBuyerClientId != buyerClientId;
        pendingWeaponIndex = weaponIndex;
        pendingBuyerClientId = buyerClientId;
        pendingRevealEndsAt = revealEndsAt;
        pendingOwnerEndsAt = ownerEndsAt;
        pendingExpiresAt = expiresAt;
        localPlayerIsPendingBuyer = showRevealForLocalPlayer;

        if (weaponIndex == TeddyPendingIndex)
        {
            // Teddy: EVERY player sees the roll + "BOX MOVING" result (not just the buyer).
            if (changed)
            {
                ShowTeddyRevealSafe(GetWeaponPool());
            }
            return;
        }

        if (weaponIndex < 0)
        {
            ClearPendingPrize();
            return;
        }

        if (changed && showRevealForLocalPlayer)
        {
            ShowRevealSafe(GetPendingPrizeName(), GetWeaponPool());
        }
    }

    // Begin a Teddy Bear result: the 2s roll, then a clear "BOX MOVING" hold, then (on expiry)
    // the box relocates. Uses the same synced pending-state pipeline as weapon prizes.
    public void BeginPendingTeddy(ulong buyerClientId)
    {
        double now = NetworkGameplayCoordinator.SharedTime;
        double resultEnds = now + RevealDuration + TeddyResultDuration;
        ApplyPendingPrizeState(
            TeddyPendingIndex,
            buyerClientId,
            now + RevealDuration, // roll ends
            resultEnds,           // ownerEndsAt (unused for Teddy)
            resultEnds,           // expiresAt -> relocate
            true);
    }

    public bool TryClaimPendingPrize(ulong claimantClientId, out int weaponIndex, out bool expired)
    {
        weaponIndex = -1;
        expired = false;
        if (!HasPendingPrize)
        {
            return false;
        }

        double now = NetworkGameplayCoordinator.SharedTime;
        if (now >= pendingExpiresAt)
        {
            ClearPendingPrize();
            expired = true;
            return false;
        }
        if (now < pendingRevealEndsAt ||
            (now < pendingOwnerEndsAt && claimantClientId != pendingBuyerClientId))
        {
            return false;
        }

        weaponIndex = pendingWeaponIndex;
        ClearPendingPrize();
        return true;
    }

    public bool ClearPendingPrizeIfExpired()
    {
        if (!IsResolvingPrize || NetworkGameplayCoordinator.SharedTime < pendingExpiresAt)
        {
            return false;
        }

        ClearPendingPrize();
        return true;
    }

    private bool CanLocalPlayerClaimPrize()
    {
        double now = NetworkGameplayCoordinator.SharedTime;
        return now >= pendingRevealEndsAt && now < pendingExpiresAt &&
            (now >= pendingOwnerEndsAt || localPlayerIsPendingBuyer);
    }

    private void ClearPendingPrize()
    {
        pendingWeaponIndex = -1;
        pendingBuyerClientId = 0;
        pendingRevealEndsAt = 0d;
        pendingOwnerEndsAt = 0d;
        pendingExpiresAt = 0d;
        localPlayerIsPendingBuyer = false;
    }

    private string GetPendingPrizeName()
    {
        List<Weapon> pool = GetWeaponPool();
        if (pendingWeaponIndex < 0 || pool.Count == 0)
        {
            return "weapon";
        }

        Weapon prize = pool[Mathf.Clamp(pendingWeaponIndex, 0, pool.Count - 1)];
        return prize != null && !string.IsNullOrEmpty(prize.weaponName) ? prize.weaponName : "weapon";
    }

    private List<Weapon> GetWeaponPool()
    {
        return weaponPool != null && weaponPool.Count > 0 ? weaponPool : BuildPool();
    }

    // Kick off the local HUD reveal, landing on the authoritative prize name. Wrapped so a
    // presentation hiccup can never affect the actual (already-completed) weapon grant.
    private static void ShowRevealSafe(string prizeName, List<Weapon> pool)
    {
        try
        {
            List<string> names = new List<string>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && !string.IsNullOrEmpty(pool[i].weaponName))
                {
                    names.Add(pool[i].weaponName);
                }
            }
            MysteryBoxRevealHud.Show(prizeName, names);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[MysteryBox] Reveal presentation skipped: " + e.Message);
        }
    }

    // Local, cosmetic Teddy reveal: the same rolling names, landing on a "BOX MOVING" result.
    // Wrapped so a presentation hiccup can never affect the (server-authoritative) relocation.
    private static void ShowTeddyRevealSafe(List<Weapon> pool)
    {
        try
        {
            List<string> names = new List<string>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && !string.IsNullOrEmpty(pool[i].weaponName))
                {
                    names.Add(pool[i].weaponName);
                }
            }
            MysteryBoxRevealHud.ShowTeddy(names);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[MysteryBox] Teddy reveal presentation skipped: " + e.Message);
        }
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
            if (!IsValidRelocationTarget(dest))
            {
                continue;
            }
            if ((dest.position - transform.position).sqrMagnitude > 0.01f)
            {
                transform.position = dest.position;
                transform.rotation = dest.rotation;
                return;
            }
        }
    }

    // A relocation marker must be a real, separate transform: never null, never the box itself,
    // and never a child of the box (which would move with it and cause a feedback loop).
    private bool IsValidRelocationTarget(Transform dest)
    {
        if (dest == null)
        {
            Debug.LogWarning("[MysteryBox] Skipping null box location marker.");
            return false;
        }
        if (dest == transform)
        {
            Debug.LogWarning("[MysteryBox] Skipping box location marker that is the MysteryBox transform itself.");
            return false;
        }
        if (dest.IsChildOf(transform))
        {
            Debug.LogWarning("[MysteryBox] Skipping box location marker '" + dest.name + "' that is a child of the MysteryBox.");
            return false;
        }
        return true;
    }

    public void ApplyNetworkTransform(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
    }
}
