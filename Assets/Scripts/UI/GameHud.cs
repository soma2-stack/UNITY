using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single consolidated on-screen HUD for "School Of The Dead" (Call of Duty
/// Zombies style), drawn entirely with legacy IMGUI (OnGUI) so it needs zero
/// scene setup — no Canvas, no prefabs.
///
/// It draws, all null-safe (any missing system is simply skipped):
///   - WEAPON (bottom-right): current weapon name + "mag / reserve" ammo, or
///     "RELOADING" while reloading. Pulled from <see cref="WeaponController"/>.
///   - CROSSHAIR (screen center): a small plus made of two boxes.
///   - ROUND (top-center): "ROUND n" from <see cref="RoundManager"/>.
///   - ZOMBIE COUNTER (under the round): "Zombies: n" where
///     n = AliveCount + RemainingToSpawn from <see cref="ZombieSpawner"/>.
///   - FOUR PLAYER POINT ICONS (top-left): slots P1..P4, each a colored box +
///     the player's points. Slot 0 defaults to the live
///     <see cref="PlayerPoints.Instance"/>; slots 1-3 are placeholders until
///     multiplayer exists. Wire real players via <see cref="SetPlayerPoints"/>.
///
/// Simply add this component to any active GameObject (e.g. a GameManager).
/// References are resolved automatically via FindFirstObjectByType.
/// </summary>
public class GameHud : MonoBehaviour
{
    private const int MaxPlayers = 4;

    [Header("Player Point Sources (up to 4)")]
    [Tooltip("Optional explicit point sources per player slot (P1..P4). " +
             "Slot 0 falls back to PlayerPoints.Instance when left empty. " +
             "Wire additional players here or via SetPlayerPoints() for multiplayer.")]
    [SerializeField] private PlayerPoints[] playerPointSources = new PlayerPoints[MaxPlayers];

    [Tooltip("Distinct color per player slot (P1..P4) used for the small icon box.")]
    [SerializeField] private Color[] playerColors =
    {
        new Color(0.20f, 0.60f, 1.00f), // P1 blue
        new Color(1.00f, 0.35f, 0.35f), // P2 red
        new Color(0.40f, 0.85f, 0.40f), // P3 green
        new Color(1.00f, 0.80f, 0.25f), // P4 yellow
    };

    [Tooltip("Force all 4 player slots to always draw (P1..P4), even empty ones. " +
             "Useful for testing the multiplayer HUD layout. Off = only slots with an " +
             "active points source are shown (so solo shows just one row).")]
    public bool alwaysShowAllSlots = false;

    [Header("Style")]
    [Tooltip("Base font size for the larger HUD labels (round / weapon).")]
    [SerializeField] private int largeFontSize = 22;
    [Tooltip("Font size for the smaller HUD labels (points / zombie counter).")]
    [SerializeField] private int smallFontSize = 16;
    [Tooltip("Crosshair half-length in pixels.")]
    [SerializeField] private float crosshairLength = 8f;
    [Tooltip("Crosshair line thickness in pixels.")]
    [SerializeField] private float crosshairThickness = 2f;

    // Cached scene references (resolved lazily, re-resolved if they go missing).
    private WeaponController weapon;
    private RoundManager round;
    private ZombieSpawner spawner;

    // Reusable styles / textures.
    private GUIStyle largeStyle;
    private GUIStyle smallStyle;
    private GUIStyle smallRightStyle;
    private GUIStyle centerStyle;
    private GUIStyle centerSmallStyle;
    private GUIStyle playerLabelStyle;
    private GUIStyle playerPointsStyle;
    private GUIStyle perkIconLabelStyle;
    private Texture2D whiteTex;

    // Gameplay scene the HUD should appear in.
    private const string GameplayScene = "SchoolOfTheDead";
    private static GameHud _runtimeInstance;

    /// <summary>
    /// Auto-spawn the HUD when the gameplay scene loads so it shows even without
    /// any manual GameManager setup. Also guarantees a PlayerPoints singleton so
    /// the P1 counter has a value. The HUD is removed when leaving gameplay so it
    /// never draws over the main menu.
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

        // Don't add a second HUD if the scene already has one wired up.
        if (_runtimeInstance != null || FindFirstObjectByType<GameHud>() != null)
        {
            return;
        }

        var go = new GameObject("GameHUD (Runtime)");
        _runtimeInstance = go.AddComponent<GameHud>();

        // Make sure points have a source so the P1 icon shows a number.
        if (PlayerPoints.Instance == null)
        {
            go.AddComponent<PlayerPoints>();
        }
    }

    /// <summary>
    /// Register (or clear) the <see cref="PlayerPoints"/> source for a given
    /// player slot (0..3). Pass null to clear the slot. Makes it easy to wire
    /// real players in when multiplayer is added.
    /// </summary>
    public void SetPlayerPoints(int slot, PlayerPoints source)
    {
        if (slot < 0 || slot >= MaxPlayers)
        {
            return;
        }

        if (playerPointSources == null || playerPointSources.Length < MaxPlayers)
        {
            ResizePlayerSources();
        }

        playerPointSources[slot] = source;
    }

    private void Awake()
    {
        ResizePlayerSources();
    }

    private void ResizePlayerSources()
    {
        if (playerPointSources != null && playerPointSources.Length == MaxPlayers)
        {
            return;
        }

        PlayerPoints[] resized = new PlayerPoints[MaxPlayers];
        if (playerPointSources != null)
        {
            int n = Mathf.Min(playerPointSources.Length, MaxPlayers);
            for (int i = 0; i < n; i++)
            {
                resized[i] = playerPointSources[i];
            }
        }
        playerPointSources = resized;
    }

    private void EnsureStyles()
    {
        if (whiteTex == null)
        {
            whiteTex = new Texture2D(1, 1);
            whiteTex.SetPixel(0, 0, Color.white);
            whiteTex.Apply();
        }

        if (largeStyle == null)
        {
            largeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = largeFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };
        }

        if (smallStyle == null)
        {
            smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = smallFontSize,
                alignment = TextAnchor.MiddleLeft,
            };
        }

        if (smallRightStyle == null)
        {
            smallRightStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = largeFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
            };
        }

        if (centerStyle == null)
        {
            centerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = largeFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        if (centerSmallStyle == null)
        {
            centerSmallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = smallFontSize,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        if (playerLabelStyle == null)
        {
            playerLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = smallFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white },
            };
        }

        if (playerPointsStyle == null)
        {
            playerPointsStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = smallFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = Color.white },
            };
        }

        if (perkIconLabelStyle == null)
        {
            perkIconLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = Mathf.Max(9, smallFontSize - 6),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white },
            };
        }
    }

    private void ResolveReferences()
    {
        if (weapon == null)
        {
            weapon = LocalPlayer.Weapon;
        }
        if (round == null)
        {
            round = FindFirstObjectByType<RoundManager>();
        }
        if (spawner == null)
        {
            spawner = FindFirstObjectByType<ZombieSpawner>();
        }
    }

    private void OnGUI()
    {
        EnsureStyles();
        ResolveReferences();

        DrawCrosshair();
        DrawRoundAndZombies();
        DrawWeapon();
        int drawnPlayers = DrawPlayerPoints();
        DrawPerkIcons(drawnPlayers);
    }

    // --- Crosshair (screen center) -----------------------------------------

    private void DrawCrosshair()
    {
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        float len = Mathf.Max(1f, crosshairLength);
        float th = Mathf.Max(1f, crosshairThickness);

        Color prev = GUI.color;
        GUI.color = Color.white;

        // Horizontal bar.
        GUI.DrawTexture(new Rect(cx - len, cy - th * 0.5f, len * 2f, th), whiteTex);
        // Vertical bar.
        GUI.DrawTexture(new Rect(cx - th * 0.5f, cy - len, th, len * 2f), whiteTex);

        GUI.color = prev;
    }

    // --- Round + zombie counter (top-center) -------------------------------

    private void DrawRoundAndZombies()
    {
        float top = 10f;
        float width = 240f;
        float x = (Screen.width - width) * 0.5f;

        if (round != null)
        {
            GUI.Label(new Rect(x, top, width, 30f), "ROUND " + Mathf.Max(0, round.CurrentRound), centerStyle);
            top += 30f;
        }

        if (spawner != null)
        {
            int left = Mathf.Max(0, spawner.AliveCount + spawner.RemainingToSpawn);
            GUI.Label(new Rect(x, top, width, 24f), "Zombies: " + left, centerSmallStyle);
        }
    }

    // --- Weapon (bottom-right) ---------------------------------------------

    private void DrawWeapon()
    {
        if (weapon == null || !weapon.HasWeapon)
        {
            return;
        }

        string text = weapon.IsReloading
            ? weapon.CurrentWeaponName + "  RELOADING"
            : weapon.CurrentWeaponName + "  " + weapon.CurrentMagazineAmmo + " / " + weapon.CurrentReserveAmmo;

        const float boxW = 320f;
        const float boxH = 32f;
        Rect rect = new Rect(Screen.width - boxW - 14f, Screen.height - boxH - 14f, boxW, boxH);
        GUI.Label(rect, text, smallRightStyle);
    }

    // --- Player point icons (top-left, stacked) ----------------------------

    private int DrawPlayerPoints()
    {
        float x      = 10f;
        float y      = 10f;
        float rowH   = 28f;
        float boxW   = 28f;
        float boxH   = 28f;
        float panelW = 160f;
        float gap    = 6f;

        GUIStyle pLabelStyle = playerLabelStyle;
        GUIStyle pointsStyle = playerPointsStyle;

        int drawn = 0;
        for (int i = 0; i < MaxPlayers; i++)
        {
            PlayerPoints source = GetPlayerSource(i);
            if (source == null && !alwaysShowAllSlots) continue;

            float rowY = y + drawn * (rowH + 5f);

            // Dark panel behind the row
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x - 2f, rowY - 2f, panelW, rowH + 4f), whiteTex);

            // Dark border behind the color box
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(new Rect(x, rowY, boxW, boxH), whiteTex);

            // Player color box (inset 1px from border)
            Color slotColor = (playerColors != null && i < playerColors.Length)
                ? playerColors[i] : Color.gray;
            GUI.color = slotColor;
            GUI.DrawTexture(new Rect(x + 1f, rowY + 1f, boxW - 2f, boxH - 2f), whiteTex);
            GUI.color = prev;

            // "P1" label inside the box
            GUI.Label(new Rect(x, rowY, boxW, boxH),
                "P" + (i + 1), pLabelStyle);

            // Points value to the right of the box
            string pointsText = source != null
                ? source.Points.ToString("N0") : "—";
            GUI.Label(new Rect(x + boxW + gap, rowY, panelW - boxW - gap, rowH),
                pointsText, pointsStyle);

            drawn++;
        }

        return drawn;
    }

    // --- Perk icon strip (below the player rows) ---------------------------

    // Per perk, in enum order: its icon color and a short 2-4 char abbreviation.
    // Uses the renamed School-Of-The-Dead perks (no legacy CoD names).
    private static readonly PerkType[] PerkOrder =
    {
        PerkType.VitalBoost,
        PerkType.ClipKick,
        PerkType.RapidRuin,
        PerkType.RescueRush,
        PerkType.SprintSurge,
        PerkType.ArmoryAmp,
    };

    private static Color PerkColor(PerkType perk)
    {
        switch (perk)
        {
            case PerkType.VitalBoost:  return new Color(0.85f, 0.15f, 0.15f); // red
            case PerkType.ClipKick:    return new Color(0.15f, 0.75f, 0.30f); // green
            case PerkType.RapidRuin:   return new Color(0.90f, 0.65f, 0.10f); // amber
            case PerkType.SprintSurge: return new Color(0.20f, 0.55f, 0.90f); // blue
            case PerkType.RescueRush:  return new Color(0.70f, 0.20f, 0.85f); // purple
            case PerkType.ArmoryAmp:   return new Color(0.80f, 0.50f, 0.15f); // orange
            default:                   return Color.gray;
        }
    }

    private static string PerkAbbrev(PerkType perk)
    {
        switch (perk)
        {
            case PerkType.VitalBoost:  return "VITL";
            case PerkType.ClipKick:    return "CLIP";
            case PerkType.RapidRuin:   return "RUIN";
            case PerkType.RescueRush:  return "RESC";
            case PerkType.SprintSurge: return "SPRT";
            case PerkType.ArmoryAmp:   return "ARMY";
            default:                   return "?";
        }
    }

    private void DrawPerkIcons(int drawnPlayers)
    {
        PerkManager pm = PerkManager.Instance;
        if (pm == null)
        {
            return;
        }

        const float iconSize = 26f;
        const float iconGap  = 6f;
        float x = 10f;
        float perkY = 10f + drawnPlayers * (28f + 5f) + 10f;

        GUIStyle iconLabel = perkIconLabelStyle;

        int drawnIcons = 0;
        foreach (PerkType perk in PerkOrder)
        {
            if (!pm.HasPerk(perk))
            {
                continue;
            }

            float iconX = x + drawnIcons * (iconSize + iconGap);

            Color prev = GUI.color;

            // 1px dark border behind the colored square.
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(iconX - 1f, perkY - 1f, iconSize + 2f, iconSize + 2f), whiteTex);

            // Colored perk square.
            GUI.color = PerkColor(perk);
            GUI.DrawTexture(new Rect(iconX, perkY, iconSize, iconSize), whiteTex);
            GUI.color = prev;

            // Tiny abbreviation label beneath the icon.
            GUI.Label(new Rect(iconX - 4f, perkY + iconSize + 1f, iconSize + 8f, 14f),
                PerkAbbrev(perk), iconLabel);

            drawnIcons++;
        }
    }

    private PlayerPoints GetPlayerSource(int slot)
    {
        if (playerPointSources != null && slot >= 0 && slot < playerPointSources.Length && playerPointSources[slot] != null)
        {
            return playerPointSources[slot];
        }

        // Slot 0 defaults to the live singleton when no explicit source is wired.
        if (slot == 0)
        {
            return PlayerPoints.Instance;
        }

        return null;
    }
}
