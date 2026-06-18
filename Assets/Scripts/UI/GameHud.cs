using UnityEngine;

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
    private Texture2D whiteTex;

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
    }

    private void ResolveReferences()
    {
        if (weapon == null)
        {
            weapon = FindFirstObjectByType<WeaponController>();
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
        DrawPlayerPoints();
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
            GUIStyle centerSmall = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(x, top, width, 24f), "Zombies: " + left, centerSmall);
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

    private void DrawPlayerPoints()
    {
        float x = 10f;
        float y = 10f;
        const float rowH = 26f;
        const float boxSize = 18f;
        const float gap = 6f;

        for (int i = 0; i < MaxPlayers; i++)
        {
            float rowY = y + i * (rowH + 4f);

            // Colored icon box for the player slot.
            Color slotColor = (playerColors != null && i < playerColors.Length) ? playerColors[i] : Color.gray;
            Color prev = GUI.color;
            GUI.color = slotColor;
            GUI.DrawTexture(new Rect(x, rowY + (rowH - boxSize) * 0.5f, boxSize, boxSize), whiteTex);
            GUI.color = prev;

            // Resolve this slot's points source. Slot 0 falls back to the live singleton.
            PlayerPoints source = GetPlayerSource(i);

            string pointsText = source != null ? source.Points.ToString() : "—";
            string label = "P" + (i + 1) + "  " + pointsText;

            GUI.Label(new Rect(x + boxSize + gap, rowY, 200f, rowH), label, smallStyle);
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
