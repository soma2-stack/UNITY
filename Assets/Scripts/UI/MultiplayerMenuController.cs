using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MultiplayerMenuController : MonoBehaviour
{
    private static readonly Color BackgroundColor = new Color(0.025f, 0.03f, 0.035f, 0.985f);
    private static readonly Color RowColor = new Color(0.105f, 0.115f, 0.12f, 0.98f);
    private static readonly Color AccentColor = new Color(0.79f, 0.13f, 0.08f, 1f);
    private static readonly Color WarmColor = new Color(0.95f, 0.73f, 0.27f, 1f);
    private static readonly Color TextColor = new Color(0.93f, 0.92f, 0.88f, 1f);
    private static readonly Color ErrorColor = new Color(1f, 0.38f, 0.28f, 1f);
    // Idle (unselected) character button colour — matches the LEAVE/BACK slate so the black
    // button label stays readable; the selected character uses AccentColor.
    private static readonly Color CharacterIdleColor = new Color(0.35f, 0.37f, 0.38f, 1f);
    // Character reserved by ANOTHER connected player — greyed out and non-clickable.
    private static readonly Color CharacterLockedColor = new Color(0.14f, 0.14f, 0.15f, 1f);

    private TMP_InputField displayNameInput;
    private TMP_InputField joinCodeInput;
    private TMP_Text statusText;
    private TMP_Text codeText;
    private TMP_Text[] rosterTexts;
    private Button hostButton;
    private Button joinButton;
    private Button startButton;
    private Button copyButton;
    private Button leaveButton;
    private Button reconnectButton;
    private Button backButton;
    private Button[] characterButtons;
    private GameObject characterDropdownList;
    private GameObject dropdownBlocker;
    private RectTransform cardRect;
    private RectTransform previewRect;
    private Image previewPortrait;
    private TMP_Text previewName;
    private TMP_Text characterChangeHint;
    private Action backAction;
    private MultiplayerSessionController session;

    // Minimum connected players required before the host can start the match.
    private const int MinimumPlayersToStart = 2;
    private const string WaitingMessage = "WAITING FOR 1 MORE SURVIVOR";

#if UNITY_EDITOR
    // Editor-only manual override for solo testing. Leave false; it is compiled out of
    // real builds entirely, so a shipped game can never start a one-player match.
    private bool editorAllowSoloStart = false;
#endif

    // Count of connected survivors currently in the roster.
    private int ConnectedPlayerCount()
    {
        if (session == null)
        {
            return 0;
        }

        int connected = 0;
        foreach (RosterEntry entry in session.Roster)
        {
            if (entry.connected)
            {
                connected++;
            }
        }
        return connected;
    }

    // True only when enough connected players are present to start (>= 2),
    // with an editor-only solo override for local testing.
    private bool HasEnoughPlayersToStart()
    {
        int connected = ConnectedPlayerCount();
#if UNITY_EDITOR
        if (editorAllowSoloStart && connected >= 1)
        {
            return true;
        }
#endif
        return connected >= MinimumPlayersToStart;
    }

    public static GameObject Create(Transform parent, Action onBack)
    {
        GameObject root = CreateUiObject("Online Co-op Overlay", parent);
        Stretch(root.GetComponent<RectTransform>());
        // Deactivate BEFORE adding the component so OnEnable does not run (and touch
        // not-yet-built UI) until the overlay is actually shown via SetActive(true).
        root.SetActive(false);
        MultiplayerMenuController controller = root.AddComponent<MultiplayerMenuController>();
        controller.backAction = onBack;
        controller.BuildInterface();
        return root;
    }

    private void OnEnable()
    {
        // Guard: if the UI has not been built yet, do nothing (avoids touching null
        // controls if OnEnable ever fires before BuildInterface).
        if (statusText == null)
        {
            return;
        }

        session = MultiplayerSessionController.Instance;
        if (session != null)
        {
            session.StateChanged += HandleStateChanged;
            session.RosterChanged += HandleRosterChanged;
            session.JoinCodeChanged += HandleJoinCodeChanged;
            session.ErrorRaised += HandleError;
            Refresh(session.State);
            HandleRosterChanged(session.Roster);
            HandleJoinCodeChanged(session.JoinCode);
        }

        if (EventSystem.current != null && displayNameInput != null)
        {
            EventSystem.current.SetSelectedGameObject(displayNameInput.gameObject);
        }
    }

    private void OnDisable()
    {
        if (session != null)
        {
            session.StateChanged -= HandleStateChanged;
            session.RosterChanged -= HandleRosterChanged;
            session.JoinCodeChanged -= HandleJoinCodeChanged;
            session.ErrorRaised -= HandleError;
        }
    }

    private async void Host()
    {
        SaveDisplayName();
        await session.HostAsync(displayNameInput.text);
    }

    private async void Join()
    {
        SaveDisplayName();
        await session.JoinAsync(joinCodeInput.text, displayNameInput.text);
    }

    private async void StartMatch()
    {
        // Safety net: never let the host start a match without enough survivors,
        // even if the button somehow gets clicked while it should be disabled.
        if (!HasEnoughPlayersToStart())
        {
            Debug.Log("Match start blocked: not enough players");
            if (statusText != null)
            {
                statusText.text = WaitingMessage;
                statusText.color = WarmColor;
            }
            return;
        }

        await session.StartMatchAsync();
    }

    private async void Leave()
    {
        await session.LeaveAsync();
    }

    private async void Reconnect()
    {
        await session.ReconnectAsync();
    }

    private void BuildInterface()
    {
        // Background: same as the loading screen (school hallway image + dark shade).
        GameObject hallway = CreateUiObject("School Hallway", transform);
        Stretch(hallway.GetComponent<RectTransform>());
        Image hallwayImg = hallway.AddComponent<Image>();
        Sprite hallwaySprite = Resources.Load<Sprite>("UI/LoadingSchoolHallway");
        if (hallwaySprite != null)
        {
            hallwayImg.sprite = hallwaySprite;
            hallwayImg.color = Color.white;
        }
        else
        {
            hallwayImg.color = BackgroundColor;
        }
        hallwayImg.preserveAspect = false;
        hallwayImg.raycastTarget = true;

        GameObject shade = CreateUiObject("Readability Shade", transform);
        Stretch(shade.GetComponent<RectTransform>());
        Image shadeImg = shade.AddComponent<Image>();
        shadeImg.color = new Color(0.01f, 0.012f, 0.015f, 0.6f);
        shadeImg.raycastTarget = true;

        // One centered dark translucent panel (the hallway + shade show around it). The dropdown
        // overlay is later parented HERE (last sibling) so it renders in front of every control.
        GameObject card = CreateUiObject("Card", transform);
        cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(880f, 980f); // centered card; fits within 1080 at 16:9
        cardRect.anchoredPosition = Vector2.zero;
        card.AddComponent<Image>().color = new Color(0.055f, 0.062f, 0.078f, 0.95f);

        // Red header bar with the title (pinned to the top of the card).
        GameObject header = CreateUiObject("Header", card.transform);
        RectTransform headerRect = header.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(0f, 92f);
        headerRect.anchoredPosition = Vector2.zero;
        header.AddComponent<Image>().color = AccentColor;

        TMP_Text title = CreateText(header.transform, "ONLINE CO-OP", 44f, FontStyles.Bold, Color.white);
        title.alignment = TextAlignmentOptions.Center;
        Stretch(title.rectTransform);

        // BACK button pinned to the bottom of the card (always visible).
        GameObject footer = CreateUiObject("Footer", card.transform);
        RectTransform footerRect = footer.GetComponent<RectTransform>();
        footerRect.anchorMin = new Vector2(0f, 0f);
        footerRect.anchorMax = new Vector2(1f, 0f);
        footerRect.pivot = new Vector2(0.5f, 0f);
        footerRect.sizeDelta = new Vector2(0f, 74f);
        footerRect.anchoredPosition = Vector2.zero;
        backButton = CreateButton(footer.transform, "BACK", () => backAction?.Invoke(), new Color(0.3f, 0.32f, 0.34f, 1f));
        RectTransform backRect = backButton.GetComponent<RectTransform>();
        backRect.anchorMin = new Vector2(0.5f, 0f);
        backRect.anchorMax = new Vector2(0.5f, 1f);
        backRect.pivot = new Vector2(0.5f, 0.5f);
        backRect.sizeDelta = new Vector2(760f, -22f);
        backRect.anchoredPosition = new Vector2(0f, 1f);

        // Scrollable middle region (between header and footer) so all controls fit.
        GameObject scroll = CreateUiObject("Scroll", card.transform);
        RectTransform scrollRect = scroll.GetComponent<RectTransform>();
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(0f, 74f);   // above footer
        scrollRect.offsetMax = new Vector2(0f, -92f);  // below header
        ScrollRect sr = scroll.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 24f;

        GameObject viewport = CreateUiObject("Viewport", scroll.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.0035f); // needed for masking
        viewport.AddComponent<RectMask2D>();
        sr.viewport = viewportRect;

        // Centered fixed-width column so controls stay readable on a full-screen panel.
        GameObject content = CreateUiObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.5f, 1f);
        contentRect.anchorMax = new Vector2(0.5f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = new Vector2(780f, 0f);
        contentRect.anchoredPosition = Vector2.zero;
        sr.content = contentRect;

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f; // roomier, more consistent spacing between sections
        layout.padding = new RectOffset(34, 34, 22, 22);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        statusText = CreateText(content.transform, "OFFLINE", 18f, FontStyles.Bold, WarmColor);
        statusText.alignment = TextAlignmentOptions.Center;
        SetHeight(statusText.gameObject, 30f);

        // How-to hint so players know what to do.
        TMP_Text hint = CreateText(content.transform,
            "HOST a game to get a 6-character code, then share it.\n" +
            "Friends pick JOIN and type the code to drop in (up to 4 players).",
            15f, FontStyles.Italic, new Color(0.78f, 0.78f, 0.74f, 1f));
        hint.alignment = TextAlignmentOptions.Center;
        hint.textWrappingMode = TextWrappingModes.Normal;
        SetHeight(hint.gameObject, 50f);

        displayNameInput = CreateInput(content.transform, "DISPLAY NAME", false);
        displayNameInput.characterLimit = 16;
        displayNameInput.text = PlayerPrefs.GetString("MultiplayerDisplayName", "Survivor");

        // Character picker: a dropdown-style selector (selected preview + expandable list). Stored
        // locally per player (PlayerPrefs via CharacterSelection) so host and client can pick
        // independently, and it drives the in-game HUD portrait (fallback: OwnerClientId-based).
        BuildCharacterSelector(content.transform);

        joinCodeInput = CreateInput(content.transform, "JOIN CODE", true);
        joinCodeInput.characterLimit = 8;

        GameObject connectionRow = CreateUiObject("Connection Actions", content.transform);
        HorizontalLayoutGroup connectionLayout = connectionRow.AddComponent<HorizontalLayoutGroup>();
        connectionLayout.spacing = 12f;
        connectionLayout.childControlWidth = true;
        connectionLayout.childControlHeight = true;
        connectionLayout.childForceExpandWidth = true;
        SetHeight(connectionRow, 62f);
        hostButton = CreateButton(connectionRow.transform, "HOST", Host, AccentColor);
        joinButton = CreateButton(connectionRow.transform, "JOIN", Join, WarmColor);

        // Join-code readout + copy, side by side.
        GameObject codeRow = CreateUiObject("Code Row", content.transform);
        HorizontalLayoutGroup codeLayout = codeRow.AddComponent<HorizontalLayoutGroup>();
        codeLayout.spacing = 12f;
        codeLayout.childControlWidth = true;
        codeLayout.childControlHeight = true;
        codeLayout.childForceExpandWidth = true;
        codeLayout.childAlignment = TextAnchor.MiddleLeft;
        SetHeight(codeRow, 52f);

        GameObject codeBox = CreateUiObject("Code Box", codeRow.transform);
        codeBox.AddComponent<Image>().color = RowColor;
        codeText = CreateText(codeBox.transform, "CODE: ------", 24f, FontStyles.Bold, TextColor);
        codeText.alignment = TextAlignmentOptions.Center;
        Stretch(codeText.rectTransform);
        copyButton = CreateButton(codeRow.transform, "COPY", CopyJoinCode, WarmColor);

        TMP_Text rosterHeading = CreateText(content.transform, "SURVIVORS", 18f, FontStyles.Bold, WarmColor);
        rosterHeading.alignment = TextAlignmentOptions.Left;
        rosterHeading.margin = new Vector4(6f, 0f, 0f, 0f);
        SetHeight(rosterHeading.gameObject, 26f);

        rosterTexts = new TMP_Text[MultiplayerSessionController.MaximumPlayers];
        for (int index = 0; index < rosterTexts.Length; index++)
        {
            GameObject row = CreateUiObject($"Roster Slot {index + 1}", content.transform);
            row.AddComponent<Image>().color = RowColor;
            SetHeight(row, 44f);

            TMP_Text slot = CreateText(row.transform, $"{index + 1}. EMPTY", 18f, FontStyles.Normal, TextColor);
            slot.alignment = TextAlignmentOptions.MidlineLeft;
            slot.margin = new Vector4(18f, 0f, 18f, 0f);
            Stretch(slot.rectTransform);
            rosterTexts[index] = slot;
        }

        startButton = CreateButton(content.transform, "START MATCH", StartMatch, AccentColor);
        leaveButton = CreateButton(content.transform, "LEAVE SESSION", Leave, new Color(0.35f, 0.37f, 0.38f, 1f));
        reconnectButton = CreateButton(content.transform, "RECONNECT", Reconnect, WarmColor);
        SetHeight(startButton.gameObject, 56f);
        SetHeight(leaveButton.gameObject, 50f);
        SetHeight(reconnectButton.gameObject, 50f);
        // NOTE: BACK lives in the pinned footer (built above) so it is always visible.

        // Character dropdown overlay: built LAST and parented to the card (outside the scroll's
        // mask) so it renders in FRONT of every control and can overlay the ones below the preview.
        BuildCharacterDropdownOverlay(card.transform);
    }

    private void HandleStateChanged(MultiplayerSessionState state)
    {
        Refresh(state);
    }

    private void Refresh(MultiplayerSessionState state)
    {
        bool busy = state == MultiplayerSessionState.Authenticating || state == MultiplayerSessionState.Hosting ||
                    state == MultiplayerSessionState.Joining || state == MultiplayerSessionState.Reconnecting ||
                    state == MultiplayerSessionState.Loading;
        bool inLobby = state == MultiplayerSessionState.Lobby;
        bool offline = state == MultiplayerSessionState.Offline || state == MultiplayerSessionState.Failed;

        statusText.color = state == MultiplayerSessionState.Failed ? ErrorColor : WarmColor;
        hostButton.interactable = offline && !busy;
        joinButton.interactable = offline && !busy;
        displayNameInput.interactable = offline && !busy;
        joinCodeInput.interactable = offline && !busy;

        // START MATCH: visible to the host in the lobby, but only clickable once there
        // are at least MinimumPlayersToStart connected survivors. While the host waits
        // alone, surface the "WAITING FOR 1 MORE SURVIVOR" hint in the status line.
        bool hostInLobby = inLobby && session != null && session.IsHost;
        bool enoughPlayers = HasEnoughPlayersToStart();
        startButton.gameObject.SetActive(hostInLobby);
        startButton.interactable = hostInLobby && enoughPlayers;

        if (hostInLobby && !enoughPlayers)
        {
            statusText.text = WaitingMessage;
            statusText.color = WarmColor;
        }
        else
        {
            statusText.text = state.ToString().ToUpperInvariant();
        }

        copyButton.gameObject.SetActive(inLobby && session != null && session.IsHost);
        leaveButton.gameObject.SetActive(inLobby);
        reconnectButton.gameObject.SetActive(state == MultiplayerSessionState.Failed && session != null && session.CanReconnect);
        backButton.interactable = !busy;
    }

    private void HandleRosterChanged(System.Collections.Generic.IReadOnlyList<RosterEntry> entries)
    {
        int connected = 0;
        if (entries != null)
        {
            foreach (RosterEntry entry in entries)
            {
                if (entry.connected)
                {
                    connected++;
                }
            }
        }
        Debug.Log("Roster count changed: " + connected + " connected players");

        for (int index = 0; index < rosterTexts.Length; index++)
        {
            if (index < entries.Count)
            {
                RosterEntry entry = entries[index];
                rosterTexts[index].text = $"{index + 1}. {entry.displayName.ToUpperInvariant()}" +
                                          (entry.connected ? string.Empty : "  [RECONNECTING]");
                rosterTexts[index].color = entry.connected ? TextColor : WarmColor;
            }
            else
            {
                rosterTexts[index].text = $"{index + 1}. EMPTY";
                rosterTexts[index].color = new Color(TextColor.r, TextColor.g, TextColor.b, 0.45f);
            }
        }

        // Re-evaluate the START MATCH gate now that the connected count may have changed
        // (e.g. enable it the moment a second survivor joins).
        if (session != null)
        {
            Refresh(session.State);
        }

        // Reservations may have changed (join/leave/select) — update character locks + local pick.
        RefreshCharacterLocks();
    }

    private void HandleJoinCodeChanged(string code)
    {
        codeText.text = "CODE: " + (string.IsNullOrWhiteSpace(code) ? "------" : code.ToUpperInvariant());
    }

    private void HandleError(string message)
    {
        statusText.text = message.ToUpperInvariant();
        statusText.color = ErrorColor;
    }

    private void CopyJoinCode()
    {
        if (session != null && !string.IsNullOrWhiteSpace(session.JoinCode))
        {
            GUIUtility.systemCopyBuffer = session.JoinCode;
            statusText.text = "JOIN CODE COPIED";
            statusText.color = WarmColor;
        }
    }

    // Build the selected-character PREVIEW row (a gold "CHARACTER" label + a clickable panel that
    // shows the chosen portrait, name and a CHANGE hint). The expandable list itself is a separate
    // overlay (see BuildCharacterDropdownOverlay) so it can render in front of the controls below.
    private void BuildCharacterSelector(Transform parent)
    {
        TMP_Text heading = CreateText(parent, "CHARACTER", 16f, FontStyles.Bold, WarmColor);
        heading.alignment = TextAlignmentOptions.Left;
        heading.margin = new Vector4(6f, 0f, 0f, 0f);
        SetHeight(heading.gameObject, 22f);

        GameObject previewGo = CreateUiObject("Selected Preview", parent);
        Image previewBg = previewGo.AddComponent<Image>();
        previewBg.color = RowColor;
        Button previewButton = previewGo.AddComponent<Button>();
        previewButton.targetGraphic = previewBg;
        previewButton.onClick.AddListener(ToggleCharacterDropdown);
        ApplyButtonColors(previewButton);
        SetHeight(previewGo, 84f);
        previewRect = previewGo.GetComponent<RectTransform>();

        previewPortrait = BuildOptionPortrait(previewGo.transform, 64f, 12f);

        previewName = CreateText(previewGo.transform, string.Empty, 24f, FontStyles.Bold, TextColor);
        previewName.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform previewNameRect = previewName.rectTransform;
        previewNameRect.anchorMin = new Vector2(0f, 0f);
        previewNameRect.anchorMax = new Vector2(1f, 1f);
        previewNameRect.pivot = new Vector2(0f, 0.5f);
        previewNameRect.offsetMin = new Vector2(92f, 0f);
        previewNameRect.offsetMax = new Vector2(-130f, 0f);

        characterChangeHint = CreateText(previewGo.transform, "CHANGE", 16f, FontStyles.Bold, WarmColor);
        characterChangeHint.alignment = TextAlignmentOptions.MidlineRight;
        RectTransform hintRect = characterChangeHint.rectTransform;
        hintRect.anchorMin = new Vector2(1f, 0f);
        hintRect.anchorMax = new Vector2(1f, 1f);
        hintRect.pivot = new Vector2(1f, 0.5f);
        hintRect.sizeDelta = new Vector2(116f, 0f);
        hintRect.anchoredPosition = new Vector2(-16f, 0f);
    }

    // Build the floating dropdown (a full-card click blocker + the option list), parented to the
    // card so it draws in FRONT of every control. Hidden until the preview is clicked; positioned
    // under the preview on open (see OpenCharacterDropdown). Options come from the character table.
    private void BuildCharacterDropdownOverlay(Transform cardParent)
    {
        // Click blocker: dims the card and closes the dropdown when clicked outside the list.
        dropdownBlocker = CreateUiObject("Dropdown Blocker", cardParent);
        Stretch(dropdownBlocker.GetComponent<RectTransform>());
        dropdownBlocker.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);
        Button blockerButton = dropdownBlocker.AddComponent<Button>();
        blockerButton.transition = Selectable.Transition.None;
        blockerButton.onClick.AddListener(CloseCharacterDropdown);

        // The floating list panel (own ContentSizeFitter sizes its height from the options).
        characterDropdownList = CreateUiObject("Character Dropdown", cardParent);
        characterDropdownList.AddComponent<Image>().color = new Color(0.09f, 0.1f, 0.12f, 1f);
        VerticalLayoutGroup listLayout = characterDropdownList.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 6f;
        listLayout.padding = new RectOffset(8, 8, 8, 8);
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;
        ContentSizeFitter listFitter = characterDropdownList.AddComponent<ContentSizeFitter>();
        listFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // width set on open
        listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        characterButtons = new Button[CharacterSelection.Count];
        for (int i = 0; i < CharacterSelection.Count; i++)
        {
            characterButtons[i] = BuildCharacterOption(characterDropdownList.transform, i);
        }

        dropdownBlocker.SetActive(false);
        characterDropdownList.SetActive(false);
        RefreshCharacterPreview();
        RefreshCharacterButtons();
    }

    // One row in the dropdown list: portrait (left) + name. Driven by the character table so the
    // name and portrait always match; the background tint marks the selected survivor.
    private Button BuildCharacterOption(Transform parent, int displayOrder)
    {
        CharacterSelection.Character character = CharacterSelection.Characters[displayOrder];

        GameObject optionGo = CreateUiObject(character.DisplayName + " Option", parent);
        Image background = optionGo.AddComponent<Image>();
        background.color = CharacterIdleColor;
        Button button = optionGo.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => SelectCharacter(character.PortraitIndex));
        ApplyButtonColors(button);
        SetHeight(optionGo, 60f);

        BuildOptionPortrait(optionGo.transform, 48f, 8f, character.PortraitIndex);

        TMP_Text name = CreateText(optionGo.transform, character.DisplayName.ToUpperInvariant(),
            18f, FontStyles.Bold, TextColor);
        name.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform nameRect = name.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0f, 0.5f);
        nameRect.offsetMin = new Vector2(66f, 0f);
        nameRect.offsetMax = new Vector2(-12f, 0f);

        return button;
    }

    // A left-anchored square portrait image. Returns the Image so the preview can re-point it.
    // Pass an index to load that portrait now; pass -1 to leave it blank for the caller to fill.
    private static Image BuildOptionPortrait(Transform parent, float size, float leftInset, int index = -1)
    {
        GameObject portraitGo = CreateUiObject("Portrait", parent);
        RectTransform rect = portraitGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = new Vector2(leftInset, 0f);
        Image image = portraitGo.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        if (index >= 0)
        {
            ApplyPortrait(image, index);
        }
        return image;
    }

    // Load Resources/HUD/player_portrait_{index} onto an Image; a missing PNG falls back to a
    // plain swatch so the menu/HUD never crash.
    private static void ApplyPortrait(Image image, int index)
    {
        Sprite sprite = Resources.Load<Sprite>("HUD/player_portrait_" + index);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
        }
        else
        {
            image.sprite = null;
            image.color = new Color(0.18f, 0.2f, 0.22f, 1f);
        }
    }

    // Open or close the dropdown depending on its current state.
    private void ToggleCharacterDropdown()
    {
        if (characterDropdownList != null && characterDropdownList.activeSelf)
        {
            CloseCharacterDropdown();
        }
        else
        {
            OpenCharacterDropdown();
        }
    }

    // Show the dropdown in FRONT of everything: activate the blocker + list, raise them to the top
    // sibling, size the list to the preview width and hang it just below the preview.
    private void OpenCharacterDropdown()
    {
        if (characterDropdownList == null || previewRect == null || cardRect == null)
        {
            return;
        }

        dropdownBlocker.SetActive(true);
        characterDropdownList.SetActive(true);
        dropdownBlocker.transform.SetAsLastSibling();
        characterDropdownList.transform.SetAsLastSibling();

        RectTransform listRect = (RectTransform)characterDropdownList.transform;
        listRect.anchorMin = new Vector2(0.5f, 0.5f);
        listRect.anchorMax = new Vector2(0.5f, 0.5f);
        listRect.pivot = new Vector2(0.5f, 1f); // top-centre pivot so it hangs downward
        listRect.sizeDelta = new Vector2(previewRect.rect.width, listRect.sizeDelta.y);

        // Convert the preview's bottom-centre (world/screen for an overlay canvas) into the card's
        // local space so the list sits right under the preview regardless of scroll position.
        Vector3[] corners = new Vector3[4];
        previewRect.GetWorldCorners(corners); // 0=BL, 1=TL, 2=TR, 3=BR
        Vector3 bottomCentreWorld = (corners[0] + corners[3]) * 0.5f;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, bottomCentreWorld);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(cardRect, screenPoint, null, out Vector2 local);
        listRect.anchoredPosition = new Vector2(local.x, local.y - 6f);

        if (characterChangeHint != null)
        {
            characterChangeHint.text = "CLOSE";
        }
    }

    // Hide the dropdown + blocker and restore the CHANGE hint.
    private void CloseCharacterDropdown()
    {
        if (dropdownBlocker != null)
        {
            dropdownBlocker.SetActive(false);
        }
        if (characterDropdownList != null)
        {
            characterDropdownList.SetActive(false);
        }
        if (characterChangeHint != null)
        {
            characterChangeHint.text = "CHANGE";
        }
    }

    // Store the chosen portrait index locally, update the preview + list highlight, close the list.
    // True once connected to a lobby, where character reservations are host-authoritative.
    private bool InNetworkedLobby => session != null && session.State == MultiplayerSessionState.Lobby;

    private void SelectCharacter(int portraitIndex)
    {
        // Never allow picking a character another connected player already holds.
        if (IsLockedByOther(portraitIndex))
        {
            CloseCharacterDropdown();
            return;
        }

        if (InNetworkedLobby)
        {
            // Host-authoritative: request the reservation. CharacterSelection + the UI update when
            // the host's roster broadcast returns (RefreshCharacterLocks), so nothing is set locally
            // until the host accepts.
            session.RequestCharacterSelect(portraitIndex);
        }
        else
        {
            // Offline lobby / not connected: purely local.
            CharacterSelection.Select(portraitIndex);
            RefreshCharacterPreview();
            RefreshCharacterButtons();
        }
        CloseCharacterDropdown();
    }

    // True when a DIFFERENT connected player currently reserves this character.
    private bool IsLockedByOther(int portraitIndex)
    {
        if (session == null || portraitIndex < 0)
        {
            return false;
        }
        var roster = session.Roster;
        if (roster == null)
        {
            return false;
        }
        ulong me = session.LocalClientId;
        foreach (RosterEntry entry in roster)
        {
            if (entry.connected && entry.clientId != me && entry.characterIndex == portraitIndex)
            {
                return true;
            }
        }
        return false;
    }

    // Mirror the host-accepted reservation for THIS player into CharacterSelection (source of truth
    // in a lobby), then refresh the preview + lock states. Called whenever the roster changes.
    private void RefreshCharacterLocks()
    {
        if (InNetworkedLobby)
        {
            ulong me = session.LocalClientId;
            var roster = session.Roster;
            if (roster != null)
            {
                foreach (RosterEntry entry in roster)
                {
                    if (entry.clientId == me && entry.characterIndex >= 0)
                    {
                        CharacterSelection.Select(entry.characterIndex);
                        break;
                    }
                }
            }
        }
        RefreshCharacterPreview();
        RefreshCharacterButtons();
    }

    // Update the selected preview panel from the stored selection (or a neutral prompt if none).
    private void RefreshCharacterPreview()
    {
        if (previewName == null || previewPortrait == null)
        {
            return;
        }

        int selected = CharacterSelection.SelectedIndex; // stored value is the portrait index
        if (selected >= 0)
        {
            previewName.text = CharacterSelection.NameOf(selected).ToUpperInvariant();
            ApplyPortrait(previewPortrait, selected);
        }
        else
        {
            previewName.text = "SELECT SURVIVOR";
            previewPortrait.sprite = null;
            previewPortrait.color = new Color(0.18f, 0.2f, 0.22f, 1f);
        }
    }

    // Highlight the selected option (AccentColor) and leave the rest idle, matching each option's
    // portrait index against the stored selection. Safe before the list exists / when none chosen.
    private void RefreshCharacterButtons()
    {
        if (characterButtons == null)
        {
            return;
        }

        int selected = CharacterSelection.SelectedIndex;
        for (int i = 0; i < characterButtons.Length; i++)
        {
            Button button = characterButtons[i];
            if (button == null)
            {
                continue;
            }
            int portraitIndex = CharacterSelection.Characters[i].PortraitIndex;
            bool lockedByOther = IsLockedByOther(portraitIndex);
            bool isSelected = portraitIndex == selected;

            // Reserved-by-another options are greyed out and non-clickable; the local player's own
            // selection stays highlighted and clickable.
            button.interactable = !lockedByOther;
            if (button.targetGraphic is Image image)
            {
                image.color = lockedByOther ? CharacterLockedColor
                    : (isSelected ? AccentColor : CharacterIdleColor);
            }
        }
    }

    // Shared button hover/press feel so the background colour we set stays visible.
    private static void ApplyButtonColors(Button button)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
    }

    private void SaveDisplayName()
    {
        string value = MultiplayerSessionController.SanitizeDisplayName(displayNameInput.text);
        displayNameInput.text = value;
        PlayerPrefs.SetString("MultiplayerDisplayName", value);
        PlayerPrefs.Save();
    }

    private static TMP_InputField CreateInput(Transform parent, string placeholderValue, bool uppercase)
    {
        GameObject root = CreateUiObject(placeholderValue, parent);
        Image background = root.AddComponent<Image>();
        background.color = RowColor;
        TMP_InputField input = root.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = uppercase ? TMP_InputField.ContentType.Alphanumeric : TMP_InputField.ContentType.Standard;

        // A code-built TMP_InputField needs a proper "Text Area" viewport (with a mask)
        // that contains the placeholder + text; without it, clicking/focusing the field
        // throws because the caret has nowhere to live. Build the standard hierarchy.
        GameObject textArea = CreateUiObject("Text Area", root.transform);
        RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(16f, 8f);
        textAreaRect.offsetMax = new Vector2(-16f, -8f);
        textArea.AddComponent<RectMask2D>();
        input.textViewport = textAreaRect;

        TMP_Text placeholder = CreateText(textArea.transform, placeholderValue, 20f, FontStyles.Normal, new Color(1f, 1f, 1f, 0.4f));
        Stretch(placeholder.rectTransform);
        input.placeholder = placeholder;

        TMP_Text text = CreateText(textArea.transform, string.Empty, 20f, FontStyles.Normal, TextColor);
        Stretch(text.rectTransform);
        input.textComponent = text;

        input.onValueChanged.AddListener(value =>
        {
            if (uppercase && value != value.ToUpperInvariant())
            {
                input.SetTextWithoutNotify(value.ToUpperInvariant());
            }
        });

        SetHeight(root, 56f);
        return input;
    }

    private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action, Color color)
    {
        GameObject root = CreateUiObject(label + " Button", parent);
        Image image = root.AddComponent<Image>();
        image.color = color;
        Button button = root.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TMP_Text text = CreateText(root.transform, label, 19f, FontStyles.Bold, Color.black);
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform);
        return button;
    }

    private static TMP_Text CreateText(Transform parent, string value, float size, FontStyles style, Color color)
    {
        GameObject root = CreateUiObject(value + " Text", parent);
        TextMeshProUGUI text = root.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.characterSpacing = 0f;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(parent, false);
        return root;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetHeight(GameObject target, float height)
    {
        LayoutElement layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
    }
}
