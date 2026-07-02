using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Custom Unity UI (Canvas + TextMeshPro) HUD for "School of the Dead" — a gritty
/// school/zombie-survival look, NOT Call of Duty. Replaces the legacy IMGUI drawing in
/// <see cref="GameHud"/> / <see cref="PerkManager"/> / <see cref="PlayerHealth"/> while
/// changing NO gameplay: it only READS existing values every frame.
///
/// Layout (Canvas Scaler: Scale With Screen Size, 1920x1080, match 0.5):
///   - Top-left     : ROUND + ZOMBIES LEFT (chalkboard placard).
///   - Top-center   : small static-ish compass strip (heading letter only).
///   - Bottom-left  : student-ID status card (points, health text, one health bar).
///   - Bottom-center: perk row (owned perks only) — the ONLY perk display.
///   - Bottom-right : notebook ammo/weapon card (name + mag / reserve).
///   - Center       : small crosshair.
///
/// It self-bootstraps in the SchoolOfTheDead scene (mirroring GameHud/PerkManager). Once the
/// canvas is built it sets display-only suppression flags on the old HUDs so nothing draws
/// twice; those flags are cleared if this HUD is ever removed, so the old HUD safely resumes.
///
/// ART LATER: every panel background is a plain <see cref="Image"/> (solid placeholder color).
/// Drop a PNG onto that Image's Sprite field (or assign one in code) to reskin a panel — the
/// text/values are separate children and are unaffected. See the color constants below.
/// </summary>
public sealed class SchoolOfTheDeadHud : MonoBehaviour
{
    private const string GameplayScene = "SchoolOfTheDead";
    private static SchoolOfTheDeadHud _instance;

    // --- Muted "school survival" palette (swap freely; panels are placeholders) ----------
    private static readonly Color OffWhite   = new Color(0.92f, 0.90f, 0.84f, 1f);
    private static readonly Color DarkSlate  = new Color(0.13f, 0.15f, 0.17f, 0.86f);
    private static readonly Color DirtyBeige = new Color(0.78f, 0.72f, 0.58f, 0.92f);
    private static readonly Color MutedYellow= new Color(0.86f, 0.74f, 0.30f, 1f);
    private static readonly Color RedAccent  = new Color(0.72f, 0.20f, 0.18f, 1f);
    private static readonly Color Teal       = new Color(0.30f, 0.62f, 0.55f, 1f);
    private static readonly Color InkDark    = new Color(0.11f, 0.12f, 0.13f, 1f);

    // --- Cached gameplay sources (READ ONLY; re-resolved each frame if missing) -----------
    private WeaponController weapon;
    private PlayerHealth health;
    private RoundManager round;
    private ZombieSpawner spawner;

    // --- Bound UI elements ----------------------------------------------------------------
    private TMP_Text roundNumberText;
    private TMP_Text zombiesLeftText;
    private TMP_Text compassText;
    private TMP_Text pointsText;
    private TMP_Text healthText;
    private RectTransform healthFill;
    private TMP_Text statusStateText; // DOWNED / DEAD banner on the status card
    private TMP_Text weaponNameText;
    private TMP_Text ammoText;

    // Perk row: one reusable slot per PerkType (enum order), shown only when owned.
    private static readonly PerkType[] PerkOrder = (PerkType[])Enum.GetValues(typeof(PerkType));
    private Image[] perkSquares;
    private TMP_Text[] perkLabels;
    private GameObject[] perkSlots;
    // Optional perk-row background art; null when no perk_row.png was found (row stays
    // transparent, as before). Only shown when at least one perk is owned.
    private Image perkRowBackground;

    // Client-side zombie count throttle (mirrors GameHud's approach).
    private float nextZombieCountRefresh;
    private int cachedZombieCount;

    private bool built;
    private bool suppressedOldHud;

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
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
            return;
        }

        if (_instance != null || FindFirstObjectByType<SchoolOfTheDeadHud>() != null)
        {
            return;
        }

        GameObject go = new GameObject("SchoolOfTheDeadHUD (Runtime)");
        _instance = go.AddComponent<SchoolOfTheDeadHud>();
    }

    private void Start()
    {
        try
        {
            BuildHud();
            built = true;
        }
        catch (Exception e)
        {
            // Fail safe: if the canvas can't be built, leave the old HUD drawing.
            Debug.LogWarning("[SchoolOfTheDeadHud] Failed to build canvas HUD; keeping legacy HUD. " + e.Message);
            return;
        }

        // Only silence the old IMGUI HUDs once the replacement is actually up.
        SuppressLegacyHud(true);
        suppressedOldHud = true;
    }

    private void OnDestroy()
    {
        if (suppressedOldHud)
        {
            SuppressLegacyHud(false); // let the legacy HUD resume if we go away
        }
        if (_instance == this)
        {
            _instance = null;
        }
    }

    // Display-only: hide the legacy duplicate drawing. No gameplay/logic is touched.
    private static void SuppressLegacyHud(bool suppress)
    {
        GameHud.SuppressDrawing = suppress;
        PerkManager.SuppressHud = suppress;
        PlayerHealth.SuppressHud = suppress;
    }

    private void Update()
    {
        if (!built)
        {
            return;
        }

        ResolveReferences();
        RefreshRoundAndZombies();
        RefreshCompass();
        RefreshStatusCard();
        RefreshPerks();
        RefreshWeapon();
    }

    private void ResolveReferences()
    {
        // Always track the LOCAL player's components (they register with LocalPlayer only
        // after spawning in multiplayer), exactly like the legacy GameHud did.
        WeaponController localWeapon = LocalPlayer.Weapon;
        if (localWeapon != null)
        {
            weapon = localWeapon;
        }
        PlayerHealth localHealth = LocalPlayer.Health;
        if (localHealth != null)
        {
            health = localHealth;
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

    // ---------------------------------------------------------------------------------------
    // Live value refresh (READ ONLY)
    // ---------------------------------------------------------------------------------------

    private void RefreshRoundAndZombies()
    {
        if (roundNumberText != null)
        {
            roundNumberText.text = round != null ? Mathf.Max(0, round.CurrentRound).ToString() : "--";
        }
        if (zombiesLeftText != null)
        {
            int left = CountZombiesForHud();
            zombiesLeftText.text = left >= 0 ? left.ToString() : "--";
        }
    }

    private int CountZombiesForHud()
    {
        bool networkedClient = NetworkManager.Singleton != null &&
                               NetworkManager.Singleton.IsListening &&
                               !NetworkManager.Singleton.IsServer;

        if (!networkedClient)
        {
            return spawner != null ? Mathf.Max(0, spawner.AliveCount + spawner.RemainingToSpawn) : -1;
        }

        // Client: the spawner doesn't run, so count the replicated living zombies (throttled).
        if (Time.unscaledTime >= nextZombieCountRefresh)
        {
            nextZombieCountRefresh = Time.unscaledTime + 0.3f;
            int alive = 0;
            ZombieAgent[] zombies = FindObjectsByType<ZombieAgent>(FindObjectsSortMode.None);
            foreach (ZombieAgent z in zombies)
            {
                if (z != null && !z.IsDead)
                {
                    alive++;
                }
            }
            cachedZombieCount = alive;
        }
        return cachedZombieCount;
    }

    private void RefreshCompass()
    {
        if (compassText == null)
        {
            return;
        }
        Transform t = LocalPlayer.Transform;
        if (t == null)
        {
            compassText.text = "N";
            return;
        }
        // 8-point heading from the player's yaw (display only).
        float yaw = t.eulerAngles.y;
        string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        int idx = Mathf.RoundToInt(yaw / 45f) & 7;
        compassText.text = dirs[idx];
    }

    private void RefreshStatusCard()
    {
        // Points (the local peer's own total; PlayerPoints publishes a per-player table).
        if (pointsText != null)
        {
            int pts = PlayerPoints.Instance != null ? PlayerPoints.Instance.Points : 0;
            pointsText.text = "$ " + pts.ToString("N0");
        }

        if (health == null)
        {
            if (healthText != null) healthText.text = "-- / --";
            if (statusStateText != null) statusStateText.gameObject.SetActive(false);
            return;
        }

        int cur = Mathf.Max(0, health.CurrentHealth);
        int max = Mathf.Max(1, health.maxHealth);
        if (healthText != null)
        {
            healthText.text = cur + " / " + max;
        }
        if (healthFill != null)
        {
            float frac = Mathf.Clamp01((float)cur / max);
            healthFill.anchorMax = new Vector2(frac, 1f);
        }

        // DOWNED / DEAD banner (replaces PlayerHealth's IMGUI status box, which is suppressed).
        if (statusStateText != null)
        {
            if (health.IsDead)
            {
                statusStateText.gameObject.SetActive(true);
                statusStateText.text = "DEAD";
                statusStateText.color = RedAccent;
            }
            else if (health.IsDowned)
            {
                statusStateText.gameObject.SetActive(true);
                statusStateText.text = "DOWNED  " + Mathf.CeilToInt(health.BleedOutRemaining) + "s";
                statusStateText.color = MutedYellow;
            }
            else
            {
                statusStateText.gameObject.SetActive(false);
            }
        }
    }

    private void RefreshPerks()
    {
        if (perkSlots == null)
        {
            return;
        }

        PerkManager pm = PerkManager.Instance;
        int ownedCount = 0;
        for (int i = 0; i < PerkOrder.Length; i++)
        {
            PerkType perk = PerkOrder[i];
            bool owned = pm != null && pm.HasPerk(perk);
            if (perkSlots[i].activeSelf != owned)
            {
                perkSlots[i].SetActive(owned);
            }
            if (owned)
            {
                ownedCount++;
                // Placeholder square coloured per perk; swap for a PNG on perkSquares[i].sprite.
                perkSquares[i].color = PerkManager.PerkColor(perk);
                perkLabels[i].text = PerkManager.PerkAbbreviation(perk);
            }
        }

        // Show the perk-strip background art only when art exists AND at least one perk is
        // owned, so an empty strip never shows a stray panel.
        if (perkRowBackground != null && perkRowBackground.enabled != (ownedCount > 0))
        {
            perkRowBackground.enabled = ownedCount > 0;
        }
    }

    private void RefreshWeapon()
    {
        bool hasWeapon = weapon != null && weapon.HasWeapon;
        if (weaponNameText != null)
        {
            weaponNameText.text = hasWeapon ? weapon.CurrentWeaponName : "—";
        }
        if (ammoText != null)
        {
            if (!hasWeapon)
            {
                ammoText.text = "-- / --";
            }
            else if (weapon.IsReloading)
            {
                ammoText.text = "RELOADING";
            }
            else
            {
                ammoText.text = weapon.CurrentMagazineAmmo + " / " + weapon.CurrentReserveAmmo;
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Canvas construction (runtime; no prefab required)
    // ---------------------------------------------------------------------------------------

    private void BuildHud()
    {
        // Root canvas + scaler.
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = canvas.GetComponent<RectTransform>();

        BuildRoundPanel(root);
        BuildCompass(root);
        BuildStatusCard(root);
        BuildPerkRow(root);
        BuildAmmoPanel(root);
        BuildCrosshair(root);
    }

    // Top-left chalkboard placard: ROUND + ZOMBIES LEFT.
    private void BuildRoundPanel(RectTransform root)
    {
        RectTransform panel = MakePanel("RoundPanel", root, DarkSlate,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(16f, -16f), new Vector2(230f, 92f));
        ApplyPanelSprite(panel.GetComponent<Image>(), "round_panel");

        MakeLabel("RoundCaption", panel, "ROUND", MutedYellow, 20, TextAlignmentOptions.TopLeft,
            new Vector2(12f, -8f), new Vector2(120f, 24f), new Vector2(0f, 1f));
        roundNumberText = MakeLabel("RoundValue", panel, "--", OffWhite, 34, TextAlignmentOptions.TopLeft,
            new Vector2(12f, -28f), new Vector2(120f, 40f), new Vector2(0f, 1f));

        MakeLabel("ZombiesCaption", panel, "ZOMBIES LEFT", OffWhite, 13, TextAlignmentOptions.BottomRight,
            new Vector2(-10f, 8f), new Vector2(150f, 18f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
        zombiesLeftText = MakeLabel("ZombiesValue", panel, "--", RedAccent, 26, TextAlignmentOptions.TopRight,
            new Vector2(-10f, 30f), new Vector2(80f, 30f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
    }

    // Top-center compass strip (simple heading letter).
    private void BuildCompass(RectTransform root)
    {
        RectTransform panel = MakePanel("CompassPanel", root, DarkSlate,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -16f), new Vector2(160f, 34f));
        // Center tick (teal highlight).
        RectTransform tick = MakeChildImage("Tick", panel, Teal);
        tick.anchorMin = new Vector2(0.5f, 0f);
        tick.anchorMax = new Vector2(0.5f, 1f);
        tick.pivot = new Vector2(0.5f, 0.5f);
        tick.sizeDelta = new Vector2(2f, 0f);
        tick.anchoredPosition = Vector2.zero;

        compassText = MakeLabel("CompassHeading", panel, "N", OffWhite, 18, TextAlignmentOptions.Center,
            Vector2.zero, new Vector2(60f, 26f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
    }

    // Bottom-left student-ID status card: points, health text, one health bar.
    private void BuildStatusCard(RectTransform root)
    {
        RectTransform panel = MakePanel("StatusCard", root, DirtyBeige,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(16f, 16f), new Vector2(270f, 100f));
        ApplyPanelSprite(panel.GetComponent<Image>(), "status_panel");

        // Little "ID" tab accent.
        RectTransform tab = MakeChildImage("IDTab", panel, RedAccent);
        tab.anchorMin = new Vector2(0f, 1f);
        tab.anchorMax = new Vector2(0f, 1f);
        tab.pivot = new Vector2(0f, 1f);
        tab.sizeDelta = new Vector2(70f, 20f);
        tab.anchoredPosition = new Vector2(10f, -6f);
        MakeLabel("IDLabel", tab.GetComponent<RectTransform>(), "STUDENT", OffWhite, 11, TextAlignmentOptions.Center,
            Vector2.zero, new Vector2(70f, 20f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

        pointsText = MakeLabel("Points", panel, "$ 0", InkDark, 24, TextAlignmentOptions.TopLeft,
            new Vector2(12f, -30f), new Vector2(180f, 30f), new Vector2(0f, 1f));

        healthText = MakeLabel("HealthText", panel, "-- / --", InkDark, 16, TextAlignmentOptions.BottomLeft,
            new Vector2(12f, 30f), new Vector2(160f, 20f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f));

        // Health bar: dark track + red fill whose right anchor is driven by health fraction.
        RectTransform track = MakeChildImage("HealthTrack", panel, InkDark);
        track.anchorMin = new Vector2(0f, 0f);
        track.anchorMax = new Vector2(0f, 0f);
        track.pivot = new Vector2(0f, 0f);
        track.sizeDelta = new Vector2(246f, 16f);
        track.anchoredPosition = new Vector2(12f, 10f);

        healthFill = MakeChildImage("HealthFill", track, RedAccent);
        healthFill.anchorMin = new Vector2(0f, 0f);
        healthFill.anchorMax = new Vector2(1f, 1f);
        healthFill.pivot = new Vector2(0f, 0f);
        healthFill.offsetMin = new Vector2(2f, 2f);
        healthFill.offsetMax = new Vector2(-2f, -2f);

        statusStateText = MakeLabel("StatusState", panel, "", RedAccent, 18, TextAlignmentOptions.Center,
            new Vector2(0f, 6f), new Vector2(200f, 24f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
        statusStateText.gameObject.SetActive(false);
    }

    // Bottom-center perk row (owned perks only). One reusable slot per PerkType.
    private void BuildPerkRow(RectTransform root)
    {
        GameObject rowGo = new GameObject("PerkRow", typeof(RectTransform));
        RectTransform row = rowGo.GetComponent<RectTransform>();
        row.SetParent(root, false);
        row.anchorMin = new Vector2(0.5f, 0f);
        row.anchorMax = new Vector2(0.5f, 0f);
        row.pivot = new Vector2(0.5f, 0f);
        row.anchoredPosition = new Vector2(0f, 16f);

        // Optional background art for the whole perk strip. Placed on the row object itself so
        // it sits BEHIND the perk slots (the layout group arranges the slots, not this graphic)
        // and auto-sizes to the row via the ContentSizeFitter + layout padding. If perk_row.png
        // is missing it is disabled, leaving the row transparent exactly as before.
        Image rowBg = rowGo.AddComponent<Image>();
        rowBg.raycastTarget = false;
        rowBg.enabled = false; // toggled on in RefreshPerks only when art loaded + perks owned
        perkRowBackground = ApplyPanelSprite(rowBg, "perk_row") ? rowBg : null;

        HorizontalLayoutGroup layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        // Padding gives the background art a small margin around the perk squares. It only adds
        // (invisible) space when no art is present, so the compact layout is preserved.
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.childAlignment = TextAnchor.LowerCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = rowGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        int n = PerkOrder.Length;
        perkSlots = new GameObject[n];
        perkSquares = new Image[n];
        perkLabels = new TMP_Text[n];

        for (int i = 0; i < n; i++)
        {
            GameObject slot = new GameObject("Perk_" + PerkOrder[i], typeof(RectTransform));
            RectTransform slotRt = slot.GetComponent<RectTransform>();
            slotRt.SetParent(row, false);
            slotRt.sizeDelta = new Vector2(40f, 52f);
            // LayoutElement so the HorizontalLayoutGroup + ContentSizeFitter can measure and
            // centre the row (a bare RectTransform reports no preferred size and collapses it).
            LayoutElement le = slot.AddComponent<LayoutElement>();
            le.preferredWidth = 40f;
            le.preferredHeight = 52f;

            RectTransform square = MakeChildImage("Square", slotRt, Color.gray);
            square.anchorMin = new Vector2(0.5f, 1f);
            square.anchorMax = new Vector2(0.5f, 1f);
            square.pivot = new Vector2(0.5f, 1f);
            square.sizeDelta = new Vector2(36f, 36f);
            square.anchoredPosition = Vector2.zero;

            TMP_Text label = MakeLabel("Label", slotRt, "", OffWhite, 11, TextAlignmentOptions.Top,
                new Vector2(0f, 2f), new Vector2(48f, 14f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));

            perkSlots[i] = slot;
            perkSquares[i] = square.GetComponent<Image>();
            perkLabels[i] = label;
            slot.SetActive(false);
        }
    }

    // Bottom-right notebook ammo/weapon card.
    private void BuildAmmoPanel(RectTransform root)
    {
        RectTransform panel = MakePanel("AmmoPanel", root, OffWhite,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-16f, 16f), new Vector2(250f, 86f));
        ApplyPanelSprite(panel.GetComponent<Image>(), "ammo_panel");

        // Red "margin line" accent like notebook paper.
        RectTransform margin = MakeChildImage("Margin", panel, RedAccent);
        margin.anchorMin = new Vector2(0f, 0f);
        margin.anchorMax = new Vector2(0f, 1f);
        margin.pivot = new Vector2(0f, 0.5f);
        margin.sizeDelta = new Vector2(3f, -12f);
        margin.anchoredPosition = new Vector2(14f, 0f);

        weaponNameText = MakeLabel("WeaponName", panel, "—", InkDark, 20, TextAlignmentOptions.TopLeft,
            new Vector2(24f, -10f), new Vector2(214f, 28f), new Vector2(0f, 1f));
        ammoText = MakeLabel("Ammo", panel, "-- / --", RedAccent, 30, TextAlignmentOptions.BottomRight,
            new Vector2(-12f, 10f), new Vector2(214f, 38f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
    }

    private void BuildCrosshair(RectTransform root)
    {
        RectTransform h = MakeChildImage("CrosshairH", root, new Color(1f, 1f, 1f, 0.75f));
        h.anchorMin = h.anchorMax = h.pivot = new Vector2(0.5f, 0.5f);
        h.sizeDelta = new Vector2(16f, 2f);
        h.anchoredPosition = Vector2.zero;

        RectTransform v = MakeChildImage("CrosshairV", root, new Color(1f, 1f, 1f, 0.75f));
        v.anchorMin = v.anchorMax = v.pivot = new Vector2(0.5f, 0.5f);
        v.sizeDelta = new Vector2(2f, 16f);
        v.anchoredPosition = Vector2.zero;
    }

    // ---------------------------------------------------------------------------------------
    // Small UI builder helpers
    // ---------------------------------------------------------------------------------------

    // Anchored panel with a background Image (the swappable placeholder).
    private static RectTransform MakePanel(string name, RectTransform parent, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    // Try to swap a panel's placeholder colour for an optional PNG in Resources/HUD. Returns
    // true if the sprite was found and applied. On miss it logs ONE warning and leaves the
    // existing placeholder colour untouched, so the HUD never breaks when art is absent.
    private static bool ApplyPanelSprite(Image img, string spriteName)
    {
        if (img == null)
        {
            return false;
        }

        Sprite sprite = Resources.Load<Sprite>("HUD/" + spriteName);
        if (sprite == null)
        {
            Debug.LogWarning("[SchoolOfTheDeadHud] Optional HUD art 'Resources/HUD/" + spriteName +
                             "' not found (import the PNG as Sprite (2D and UI)); keeping placeholder panel.");
            return false;
        }

        img.sprite = sprite;
        img.color = Color.white;       // show the art's own colours + alpha (preserve transparency)
        img.preserveAspect = false;    // panels fill their anchored rect
        // Use 9-slice only when the sprite actually defines borders; otherwise stretch simply.
        img.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        return true;
    }

    private static RectTransform MakeChildImage(string name, RectTransform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return rt;
    }

    private static TMP_Text MakeLabel(string name, RectTransform parent, string text, Color color, int fontSize,
        TextAlignmentOptions align, Vector2 anchoredPos, Vector2 size,
        Vector2 pivot, Vector2? anchorMin = null, Vector2? anchorMax = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin ?? pivot;
        rt.anchorMax = anchorMax ?? pivot;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = color;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = align;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        return tmp;
    }
}
