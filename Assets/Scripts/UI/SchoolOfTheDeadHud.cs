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
    private static readonly Color InkDark    = new Color(0.11f, 0.12f, 0.13f, 1f);

    // --- Layout tuning (1920x1080 reference resolution; adjust these to taste) ------------
    private const float ScreenMargin = 24f;   // safe gap from the screen edges
    private const float PanelPad     = 24f;   // inner inset so text never touches panel edges
    private static readonly Vector2 RoundPanelSize  = new Vector2(362f, 160f); // slightly taller so the lower row can rise without touching the number
    private static readonly Vector2 StatusPanelSize = new Vector2(576f, 226f); // ~7% smaller than before (was too big)
    private static readonly Vector2 AmmoPanelSize   = new Vector2(280f, 96f);  // +12% (was too small/cramped)

    // --- Round panel content regions (nudge to line up with the chalkboard art frame) -----
    private const float RoundInsetX        = 58f; // horizontal inset from the chalkboard frame (in from edges)
    private const float RoundTopInset      = 32f; // gap from the top frame to the round NUMBER's row (unchanged; number stays put)
    private const float RoundCaptionTopInset = 56f; // gap from the top frame to the ROUND label (decoupled from the number so the
                                                      // label can sit lower/more-centred in the upper area without moving the number)
    private const float RoundBotInset      = 30f; // gap from the bottom frame to the lower row (pushes ZOMBIES LEFT/count up)
    private const int   RoundCaptionFont   = 28;  // "ROUND" label size (bigger); ZOMBIES LEFT uses RoundCaptionFont-6
    private const int   RoundNumberFont    = 50;  // big round number
    private const int   RoundZombiesFont   = 34;  // zombie count

    // --- Status card content regions (nudge to line up with the student-ID art) -----------
    // The left portion of the art is the portrait + STUDENT tab, so all live content sits in
    // the RIGHT region (x >= StatusContentLeft). Points go in the upper-right box; the health
    // bar + a small health-number box share the lower-right row.
    private const float StatusContentLeft = 214f; // health bar start X (independent of points; portrait sits left of it) - scaled with the smaller card
    private const float StatusRightPad    = 24f;  // gap from the panel's right edge
    private const int   StatusPointsFont  = 40;   // points value size (bigger = more readable)
    private const float StatusPointsW     = 252f; // points box width (centre-aligned value sits in the middle of this box) - a touch wider so the centre sits slightly further LEFT in the black rectangle
    private const float StatusPointsH     = 56f;  // points box height
    private const float StatusPointsTop   = 65f;  // gap from the panel top to the points box (seats the value in the black points rectangle)
    private const int   StatusHealthFont  = 24;   // health-number size (small box)
    private const float StatusHealthRowY  = 56f;  // health row centre height above the panel bottom - scaled with the smaller card
    private const float StatusHealthBarH  = 20f;  // health bar/slot thickness
    private const float StatusHealthBarW  = 216f; // health bar width - extended to fill the slot better (still clamped to leave an 8px gap before the number box)
    private const float StatusHealthNumW  = 114f; // small health-number box width at the right end - scaled with the smaller card

    // --- Cached gameplay sources (READ ONLY; re-resolved each frame if missing) -----------
    private WeaponController weapon;
    private PlayerHealth health;
    private RoundManager round;
    private ZombieSpawner spawner;

    // --- Bound UI elements ----------------------------------------------------------------
    private TMP_Text roundNumberText;
    private TMP_Text zombiesLeftText;
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

    private void RefreshStatusCard()
    {
        // Points (the local peer's own total; PlayerPoints publishes a per-player table).
        if (pointsText != null)
        {
            int pts = PlayerPoints.Instance != null ? PlayerPoints.Instance.Points : 0;
            pointsText.text = pts.ToString("N0"); // no "$" - the art already has a points icon/label
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
            new Vector2(ScreenMargin, -ScreenMargin), RoundPanelSize);
        ApplyPanelSprite(panel.GetComponent<Image>(), "round", "round_panel");

        // ROUND caption sits lower/more-centred in the upper area (its own RoundCaptionTopInset,
        // decoupled from the round number's row) so it doesn't hug the top-left corner; the
        // round number stays on the right, unmoved, at RoundTopInset.
        MakeLabel("RoundCaption", panel, "ROUND", MutedYellow, RoundCaptionFont, TextAlignmentOptions.TopLeft,
            new Vector2(RoundInsetX, -RoundCaptionTopInset), new Vector2(140f, 34f), new Vector2(0f, 1f));
        roundNumberText = MakeLabel("RoundValue", panel, "--", OffWhite, RoundNumberFont, TextAlignmentOptions.Right,
            new Vector2(-RoundInsetX, -RoundTopInset), new Vector2(120f, 58f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));

        // ZOMBIES LEFT lower-left; count lower-right in red.
        MakeLabel("ZombiesCaption", panel, "ZOMBIES LEFT", OffWhite, RoundCaptionFont - 6, TextAlignmentOptions.BottomLeft,
            new Vector2(RoundInsetX, RoundBotInset), new Vector2(180f, 22f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f));
        zombiesLeftText = MakeLabel("ZombiesValue", panel, "--", RedAccent, RoundZombiesFont, TextAlignmentOptions.BottomRight,
            new Vector2(-RoundInsetX, RoundBotInset - 4f), new Vector2(110f, 44f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
    }

    // Bottom-left student-ID status card: points, health text, one health bar.
    private void BuildStatusCard(RectTransform root)
    {
        RectTransform panel = MakePanel("StatusCard", root, DirtyBeige,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(ScreenMargin, ScreenMargin), StatusPanelSize);
        ApplyPanelSprite(panel.GetComponent<Image>(), "status", "status_panel");

        // Health-bar width, clamped so it always leaves room for the number box on the right
        // and never runs into the portrait region on the left.
        float healthBarRight = StatusContentLeft + StatusHealthBarW;                 // bar's right edge (from left)
        float numberBoxLeft  = StatusPanelSize.x - StatusRightPad - StatusHealthNumW; // number box's left edge
        float healthBarW = Mathf.Min(StatusHealthBarW, numberBoxLeft - 8f - StatusContentLeft);

        // POINTS — centred (both axes) inside the upper-right points box.
        pointsText = MakeLabel("Points", panel, "0", InkDark, StatusPointsFont, TextAlignmentOptions.Center,
            new Vector2(-StatusRightPad, -StatusPointsTop), new Vector2(StatusPointsW, StatusPointsH),
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));

        // HEALTH BAR — narrow track in the lower-right row (left of the number box), fill inset
        // so it never spills outside the frame.
        RectTransform track = MakeChildImage("HealthTrack", panel, InkDark);
        track.anchorMin = new Vector2(0f, 0f);
        track.anchorMax = new Vector2(0f, 0f);
        track.pivot = new Vector2(0f, 0f);
        track.sizeDelta = new Vector2(Mathf.Max(60f, healthBarW), StatusHealthBarH);
        track.anchoredPosition = new Vector2(StatusContentLeft, StatusHealthRowY);

        healthFill = MakeChildImage("HealthFill", track, RedAccent);
        healthFill.anchorMin = new Vector2(0f, 0f);
        healthFill.anchorMax = new Vector2(1f, 1f);
        healthFill.pivot = new Vector2(0f, 0f);
        healthFill.offsetMin = new Vector2(2f, 2f);
        healthFill.offsetMax = new Vector2(-2f, -2f);

        // HEALTH NUMBER — small box at the right end of the health row, vertically centred on
        // the bar (bottom -6, height +12 puts its centre on the bar's centre).
        healthText = MakeLabel("HealthText", panel, "-- / --", InkDark, StatusHealthFont, TextAlignmentOptions.Center,
            new Vector2(-StatusRightPad, StatusHealthRowY - 6f), new Vector2(StatusHealthNumW, StatusHealthBarH + 12f),
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));

        // DOWNED / DEAD banner ABOVE the card so it never covers the status art.
        statusStateText = MakeLabel("StatusState", panel, "", RedAccent, 26, TextAlignmentOptions.Left,
            new Vector2(0f, 8f), new Vector2(StatusPanelSize.x, 32f), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 1f));
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
        perkRowBackground = ApplyPanelSprite(rowBg, "Perk", "perk_row", "perk") ? rowBg : null;

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
            new Vector2(-ScreenMargin, ScreenMargin), AmmoPanelSize);
        ApplyPanelSprite(panel.GetComponent<Image>(), "ammo", "ammo_panel");

        float innerW = AmmoPanelSize.x - 24f;

        // Weapon name across the top, ammo centered below — both centered inside the art.
        weaponNameText = MakeLabel("WeaponName", panel, "—", InkDark, 20, TextAlignmentOptions.Top,
            new Vector2(0f, -8f), new Vector2(innerW, 26f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        ammoText = MakeLabel("Ammo", panel, "-- / --", RedAccent, 34, TextAlignmentOptions.Center,
            new Vector2(0f, 12f), new Vector2(innerW, 44f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
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

    // Try to swap a panel's placeholder colour for an optional PNG in Resources/HUD. Tries each
    // candidate name in order (so it works whether the art is named e.g. "round" or
    // "round_panel") and uses the first that loads. Returns true if a sprite was applied. On a
    // full miss it logs ONE warning and leaves the placeholder colour untouched, so the HUD
    // never breaks when art is absent.
    private static bool ApplyPanelSprite(Image img, params string[] spriteNames)
    {
        if (img == null || spriteNames == null)
        {
            return false;
        }

        Sprite sprite = null;
        foreach (string spriteName in spriteNames)
        {
            sprite = Resources.Load<Sprite>("HUD/" + spriteName);
            if (sprite != null)
            {
                break;
            }
        }

        if (sprite == null)
        {
            Debug.LogWarning("[SchoolOfTheDeadHud] Optional HUD art not found in Resources/HUD (tried: " +
                             string.Join(", ", spriteNames) +
                             "; import the PNG as Sprite (2D and UI)); keeping placeholder panel.");
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
