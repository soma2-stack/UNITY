using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tracks which perks the local player owns and applies their gameplay effects
/// (Call of Duty Zombies style). Singleton, accessed via <see cref="Instance"/>.
///
/// Like <c>GameHud</c>, this self-bootstraps in the "SchoolOfTheDead" gameplay
/// scene so perks work with zero manual scene setup. It is removed when leaving
/// gameplay so it never lingers over the main menu.
///
/// Effects are applied by finding the player's components at runtime
/// (FindAnyObjectByType) and setting multipliers / flags on them:
///   - Juggernog   -> PlayerHealth.SetMaxHealth (raise max + heal to full)
///   - SpeedCola   -> WeaponController.reloadSpeedMultiplier (faster reloads)
///   - DoubleTap   -> WeaponController.fireRateMultiplier (faster firing)
///   - StaminUp    -> PlayerMovement.speedMultiplier (faster movement)
///   - QuickRevive -> read by PlayerHealth for a solo self-revive (no stat here)
///   - MuleKick    -> bumps <see cref="ExtraWeaponSlots"/> (a flag others can read)
///
/// All lookups are null-safe: granting a perk whose player component is missing
/// still records ownership so the effect applies once the component appears
/// (re-applied on every grant and on scene load).
/// </summary>
public class PerkManager : MonoBehaviour
{
    public static PerkManager Instance { get; private set; }
    private static readonly Dictionary<ulong, HashSet<PerkType>> serverPerksByClient = new Dictionary<ulong, HashSet<PerkType>>();

    public static IReadOnlyDictionary<ulong, HashSet<PerkType>> ServerPerks => serverPerksByClient;

    [Header("Juggernog")]
    [Tooltip("Maximum health the player is raised to when Juggernog is bought.")]
    public int juggernogMaxHealth = 250;

    [Header("Speed Cola")]
    [Tooltip("Reload-speed multiplier applied to the WeaponController (higher = faster).")]
    public float speedColaReloadMultiplier = 2f;

    [Header("Double Tap")]
    [Tooltip("Fire-rate multiplier applied to the WeaponController (higher = faster).")]
    public float doubleTapFireRateMultiplier = 1.5f;

    [Header("Stamin-Up")]
    [Tooltip("Movement-speed multiplier applied to PlayerMovement (higher = faster).")]
    public float staminUpSpeedMultiplier = 1.35f;

    [Header("Down / Revive")]
    [Tooltip("Solo mode: Quick Revive is protected from the random perk loss on revive " +
             "(it drives the solo self-revive). Turn off for co-op so any perk can be lost.")]
    public bool SoloMode = true;

    [Header("Perk Icons (assign in Inspector)")]
    [Tooltip("Custom icon textures shown in the bottom HUD strip. " +
             "Slots: [0] VitalBoost, [1] ClipKick, [2] RapidRuin, " +
             "[3] RescueRush, [4] SprintSurge, [5] ArmoryAmp. " +
             "Leave a slot empty to fall back to the colored square.")]
    public Texture2D[] perkIcons = new Texture2D[6];

    [Header("Limits")]
    [Tooltip("Max simultaneous perks (classic base Zombies = 4). Set 0 for unlimited.")]
    public int maxPerks = 4;

    /// <summary>Raised whenever the owned-perk set changes.</summary>
    public event Action OnPerksChanged;

    private readonly HashSet<PerkType> ownedPerks = new HashSet<PerkType>();

    /// <summary>
    /// Number of EXTRA weapon slots granted by Mule Kick (0 normally, 1 with the
    /// perk). A weapon-pickup system can read this to allow one more weapon.
    /// </summary>
    public int ExtraWeaponSlots { get; private set; }

    // Cached player components (resolved lazily, re-resolved if they go missing).
    private PlayerHealth playerHealth;
    private WeaponController weaponController;
    private PlayerMovement playerMovement;

    // Gameplay scene the perk system should be active in.
    private const string GameplayScene = "SchoolOfTheDead";
    private static PerkManager _runtimeInstance;

    /// <summary>
    /// Auto-spawn the perk manager when the gameplay scene loads (mirrors GameHud)
    /// so perk machines have something to grant into without manual setup.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnAnySceneLoaded;
        SceneManager.sceneLoaded += OnAnySceneLoaded;
        SpawnIfGameplayScene(SceneManager.GetActiveScene());
    }

    private static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SpawnIfGameplayScene(scene);
    }

    private static void SpawnIfGameplayScene(Scene scene)
    {
        if (scene.name != GameplayScene)
        {
            if (_runtimeInstance != null)
            {
                Destroy(_runtimeInstance.gameObject);
                _runtimeInstance = null;
            }
            return;
        }

        // Don't add a second manager if the scene already has one.
        if (_runtimeInstance != null || FindFirstObjectByType<PerkManager>() != null)
        {
            return;
        }

        var go = new GameObject("PerkManager (Runtime)");
        _runtimeInstance = go.AddComponent<PerkManager>();
    }

    private void Awake()
    {
        // Enforce a single instance.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (_runtimeInstance == null)
        {
            _runtimeInstance = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        if (_runtimeInstance == this)
        {
            _runtimeInstance = null;
        }
    }

    /// <summary>True if the local player owns the given perk.</summary>
    public bool HasPerk(PerkType perk)
    {
        return ownedPerks.Contains(perk);
    }

    /// <summary>
    /// Grant a perk and apply its effect. Returns false if it was already owned
    /// (so callers can avoid charging the player twice).
    /// </summary>
    public bool TryGrant(PerkType perk)
    {
        if (ownedPerks.Contains(perk))
        {
            return false;
        }

        // Classic Zombies caps how many perks you can hold at once (4 in the base
        // game). maxPerks <= 0 means unlimited. PerkMachine refunds when this fails.
        if (maxPerks > 0 && ownedPerks.Count >= maxPerks)
        {
            Debug.Log("[PerkManager] Perk limit reached (" + maxPerks + "); cannot buy " + perk + ".");
            return false;
        }

        ownedPerks.Add(perk);
        ApplyEffect(perk);
        Debug.Log("[PerkManager] Granted perk: " + perk);
        OnPerksChanged?.Invoke();
        return true;
    }

    public static void ServerGrantClientPerk(ulong clientId, PerkType perk)
    {
        if (!serverPerksByClient.TryGetValue(clientId, out HashSet<PerkType> perks))
        {
            perks = new HashSet<PerkType>();
            serverPerksByClient[clientId] = perks;
        }
        perks.Add(perk);
    }

    public static void ApplyNetworkPerkState(ulong clientId, PerkType perk, bool hasPerk)
    {
        if (!serverPerksByClient.TryGetValue(clientId, out HashSet<PerkType> perks))
        {
            perks = new HashSet<PerkType>();
            serverPerksByClient[clientId] = perks;
        }

        if (hasPerk)
        {
            perks.Add(perk);
        }
        else
        {
            perks.Remove(perk);
        }
    }

    public static bool ClientHasPerk(ulong clientId, PerkType perk)
    {
        return serverPerksByClient.TryGetValue(clientId, out HashSet<PerkType> perks) &&
               perks.Contains(perk);
    }

    private void ResolvePlayer()
    {
        if (playerHealth == null)
        {
            playerHealth = LocalPlayer.Health;
        }
        if (weaponController == null)
        {
            weaponController = LocalPlayer.Weapon;
        }
        if (playerMovement == null)
        {
            playerMovement = LocalPlayer.Movement;
        }
    }

    private void ApplyEffect(PerkType perk)
    {
        ResolvePlayer();

        switch (perk)
        {
            case PerkType.VitalBoost:
                // Raise max health and heal to full.
                if (playerHealth != null)
                {
                    playerHealth.SetMaxHealth(juggernogMaxHealth, true);
                }
                break;

            case PerkType.ClipKick:
                if (weaponController != null)
                {
                    weaponController.reloadSpeedMultiplier = Mathf.Max(0.01f, speedColaReloadMultiplier);
                }
                break;

            case PerkType.RapidRuin:
                if (weaponController != null)
                {
                    weaponController.fireRateMultiplier = Mathf.Max(0.01f, doubleTapFireRateMultiplier);
                }
                break;

            case PerkType.SprintSurge:
                if (playerMovement != null)
                {
                    playerMovement.speedMultiplier = Mathf.Max(0.01f, staminUpSpeedMultiplier);
                }
                break;

            case PerkType.RescueRush:
                // No stat to set here - PlayerHealth queries HasPerk(QuickRevive)
                // each frame while downed to allow a solo self-revive.
                ReviveInteraction revive = LocalPlayer.Transform != null
                    ? LocalPlayer.Transform.GetComponent<ReviveInteraction>()
                    : null;
                if (revive != null)
                {
                    revive.reviveDuration = Mathf.Max(1f, revive.reviveDuration * 0.5f);
                }
                break;

            case PerkType.ArmoryAmp:
                // Allow one extra weapon slot. WeaponController.GiveWeapon reads
                // ExtraWeaponSlots to raise the carry cap; RemoveExtraWeaponSlot()
                // trims it back down if the perk is later lost.
                ExtraWeaponSlots = 1;
                break;
        }
    }

    /// <summary>
    /// CoD Zombies: when the player goes DOWN they lose one random perk. Removes a random
    /// owned perk and reverts its gameplay effect. In <see cref="SoloMode"/> Quick Revive is
    /// protected (it drives the solo self-revive) - if it is the only perk owned, nothing is
    /// lost. Fires <see cref="OnPerksChanged"/> on removal.
    /// </summary>
    public void LoseRandomPerk()
    {
        if (ownedPerks.Count == 0)
        {
            return;
        }

        // Eligible pool: every owned perk except a solo-protected Quick Revive.
        List<PerkType> pool = new List<PerkType>();
        foreach (PerkType p in ownedPerks)
        {
            if (SoloMode && p == PerkType.RescueRush)
            {
                continue;
            }
            pool.Add(p);
        }

        if (pool.Count == 0)
        {
            return; // only a protected Quick Revive was owned
        }

        PerkType lost = pool[UnityEngine.Random.Range(0, pool.Count)];
        ownedPerks.Remove(lost);
        RevertEffect(lost);
        Debug.Log("[PerkManager] Lost perk on down: " + lost);
        OnPerksChanged?.Invoke();
    }

    /// <summary>Undo a perk's gameplay effect when it is lost (mirror of ApplyEffect).</summary>
    private void RevertEffect(PerkType perk)
    {
        ResolvePlayer();

        switch (perk)
        {
            case PerkType.VitalBoost:
                if (playerHealth != null)
                {
                    playerHealth.SetMaxHealth(100, false); // back to base max, don't heal
                }
                break;

            case PerkType.ClipKick:
                if (weaponController != null)
                {
                    weaponController.reloadSpeedMultiplier = 1f;
                }
                break;

            case PerkType.RapidRuin:
                if (weaponController != null)
                {
                    weaponController.fireRateMultiplier = 1f;
                }
                break;

            case PerkType.SprintSurge:
                if (playerMovement != null)
                {
                    playerMovement.speedMultiplier = 1f;
                }
                break;

            case PerkType.RescueRush:
                // No stat to revert.
                ReviveInteraction revive = LocalPlayer.Transform != null
                    ? LocalPlayer.Transform.GetComponent<ReviveInteraction>()
                    : null;
                if (revive != null)
                {
                    revive.reviveDuration = 4f;
                }
                break;

            case PerkType.ArmoryAmp:
                ExtraWeaponSlots = 0;
                if (weaponController != null)
                {
                    weaponController.RemoveExtraWeaponSlot();
                }
                break;
        }
    }

    // --- Owned-perk icon row (bottom-center IMGUI) -------------------------

    private GUIStyle perkLabelStyle;

    private Texture2D GetPerkIcon(PerkType perk)
    {
        if (perkIcons == null) return null;
        switch (perk)
        {
            case PerkType.VitalBoost:  return perkIcons.Length > 0 ? perkIcons[0] : null;
            case PerkType.ClipKick:    return perkIcons.Length > 1 ? perkIcons[1] : null;
            case PerkType.RapidRuin:   return perkIcons.Length > 2 ? perkIcons[2] : null;
            case PerkType.RescueRush:  return perkIcons.Length > 3 ? perkIcons[3] : null;
            case PerkType.SprintSurge: return perkIcons.Length > 4 ? perkIcons[4] : null;
            case PerkType.ArmoryAmp:   return perkIcons.Length > 5 ? perkIcons[5] : null;
            default: return null;
        }
    }

    private void OnGUI()
    {
        if (ownedPerks.Count == 0)
        {
            return;
        }

        if (perkLabelStyle == null)
        {
            perkLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        const float boxSize = 34f;
        const float gap = 8f;
        const float labelH = 14f;

        // Count owned perks to center the row along the bottom of the screen.
        int count = ownedPerks.Count;
        float totalWidth = count * boxSize + (count - 1) * gap;
        float startX = (Screen.width - totalWidth) * 0.5f;
        float y = Screen.height - boxSize - labelH - 12f;

        int i = 0;
        // Iterate in enum order for a stable on-screen layout.
        foreach (PerkType perk in (PerkType[])Enum.GetValues(typeof(PerkType)))
        {
            if (!ownedPerks.Contains(perk))
            {
                continue;
            }

            float x = startX + i * (boxSize + gap);

            Color prev = GUI.color;
            Texture2D icon = GetPerkIcon(perk);
            Texture2D tex  = icon != null ? icon : Texture2D.whiteTexture;
            GUI.color      = icon != null ? Color.white : PerkColor(perk);
            GUI.DrawTexture(new Rect(x, y, boxSize, boxSize), tex);
            GUI.color = prev;

            GUI.Label(new Rect(x - gap, y + boxSize, boxSize + gap * 2f, labelH), PerkAbbreviation(perk), perkLabelStyle);
            i++;
        }
    }

    /// <summary>Distinct display color for each perk (shared with the perk machines).</summary>
    public static Color PerkColor(PerkType perk)
    {
        switch (perk)
        {
            case PerkType.VitalBoost:   return new Color(0.85f, 0.15f, 0.15f); // red
            case PerkType.ClipKick:   return new Color(0.20f, 0.70f, 0.25f); // green
            case PerkType.RapidRuin:   return new Color(0.95f, 0.75f, 0.15f); // amber
            case PerkType.RescueRush: return new Color(0.20f, 0.55f, 0.95f); // blue
            case PerkType.SprintSurge:    return new Color(0.95f, 0.50f, 0.10f); // orange
            case PerkType.ArmoryAmp:    return new Color(0.55f, 0.30f, 0.75f); // purple
            default:                   return Color.gray;
        }
    }

    /// <summary>Short human-readable name shown under each perk icon and on machines.</summary>
    public static string PerkAbbreviation(PerkType perk)
    {
        switch (perk)
        {
            case PerkType.VitalBoost:   return "Vital";
            case PerkType.ClipKick:   return "Clip";
            case PerkType.RapidRuin:   return "Rapid";
            case PerkType.RescueRush: return "Rescue";
            case PerkType.SprintSurge:    return "Sprint";
            case PerkType.ArmoryAmp:    return "Armory";
            default:                   return perk.ToString();
        }
    }

    /// <summary>Full display name (used by perk machine prompts).</summary>
    public static string PerkDisplayName(PerkType perk)
    {
        switch (perk)
        {
            case PerkType.VitalBoost:   return "Vital Boost";
            case PerkType.ClipKick:   return "Clip Kick";
            case PerkType.RapidRuin:   return "Rapid Ruin";
            case PerkType.RescueRush: return "Rescue Rush";
            case PerkType.SprintSurge:    return "Sprint Surge";
            case PerkType.ArmoryAmp:    return "Armory Amp";
            default:                   return perk.ToString();
        }
    }

    /// <summary>Default cost for each perk (used by the perk machine placer / inspector default).</summary>
    public static int DefaultCost(PerkType perk)
    {
        switch (perk)
        {
            case PerkType.RescueRush: return 500;
            case PerkType.RapidRuin:   return 2000;
            case PerkType.SprintSurge:    return 2000;
            case PerkType.VitalBoost:   return 2500;
            case PerkType.ClipKick:   return 3000;
            case PerkType.ArmoryAmp:    return 4000;
            default:                   return 2000;
        }
    }
}
