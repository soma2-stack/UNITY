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

        // Card fills the whole screen (transparent so the hallway background shows).
        GameObject card = CreateUiObject("Card", transform);
        RectTransform cardRect = card.GetComponent<RectTransform>();
        Stretch(cardRect);
        card.AddComponent<Image>().color = new Color(0.04f, 0.045f, 0.05f, 0.35f);

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
        layout.spacing = 10f;
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

        // Character picker: choose one of the 4 survivors. Each option is a card showing the
        // character portrait + name; the selected card is highlighted. Stored locally per player
        // (PlayerPrefs via CharacterSelection) so host and client can pick independently, and it
        // drives the in-game HUD portrait (fallback: OwnerClientId-based portrait when unset).
        TMP_Text characterHeading = CreateText(content.transform, "CHOOSE YOUR SURVIVOR", 20f, FontStyles.Bold, TextColor);
        characterHeading.alignment = TextAlignmentOptions.Center;
        SetHeight(characterHeading.gameObject, 30f);

        GameObject characterRow = CreateUiObject("Character Row", content.transform);
        HorizontalLayoutGroup characterLayout = characterRow.AddComponent<HorizontalLayoutGroup>();
        characterLayout.spacing = 12f;
        characterLayout.padding = new RectOffset(0, 0, 4, 4);
        characterLayout.childControlWidth = true;
        characterLayout.childControlHeight = true;
        characterLayout.childForceExpandWidth = true;
        characterLayout.childForceExpandHeight = true;
        SetHeight(characterRow, 150f);

        characterButtons = new Button[CharacterSelection.Count];
        for (int i = 0; i < CharacterSelection.Count; i++)
        {
            characterButtons[i] = BuildCharacterCard(characterRow.transform, i);
        }
        RefreshCharacterButtons();

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

        TMP_Text rosterHeading = CreateText(content.transform, "SURVIVORS", 20f, FontStyles.Bold, TextColor);
        rosterHeading.alignment = TextAlignmentOptions.Center;
        SetHeight(rosterHeading.gameObject, 34f);

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

    // Store the chosen character locally and update which button reads as selected.
    private void SelectCharacter(int index)
    {
        CharacterSelection.Select(index);
        RefreshCharacterButtons();
    }

    // Highlight the selected character button (AccentColor) and leave the rest idle. Safe to call
    // before the row exists (guards on null) and when nothing is chosen (no button highlighted).
    private void RefreshCharacterButtons()
    {
        if (characterButtons == null)
        {
            return;
        }

        int selected = CharacterSelection.SelectedIndex;
        for (int i = 0; i < characterButtons.Length; i++)
        {
            if (characterButtons[i] == null)
            {
                continue;
            }
            if (characterButtons[i].targetGraphic is Image image)
            {
                image.color = i == selected ? AccentColor : CharacterIdleColor;
            }
        }
    }

    // One clickable character card: portrait (top) + name (bottom) on a tinted background whose
    // colour RefreshCharacterButtons flips to show the selected state. A missing portrait PNG
    // falls back to a plain swatch so the menu never breaks. No gameplay or networking here.
    private Button BuildCharacterCard(Transform parent, int index)
    {
        GameObject cardGo = CreateUiObject(CharacterSelection.NameOf(index) + " Card", parent);
        Image background = cardGo.AddComponent<Image>();
        background.color = CharacterIdleColor;

        Button button = cardGo.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => SelectCharacter(index));
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white; // keep background.color visible; selection tints it
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        // Portrait near the top of the card.
        GameObject portraitGo = CreateUiObject("Portrait", cardGo.transform);
        RectTransform portraitRect = portraitGo.GetComponent<RectTransform>();
        portraitRect.anchorMin = new Vector2(0.5f, 1f);
        portraitRect.anchorMax = new Vector2(0.5f, 1f);
        portraitRect.pivot = new Vector2(0.5f, 1f);
        portraitRect.sizeDelta = new Vector2(96f, 96f);
        portraitRect.anchoredPosition = new Vector2(0f, -12f);
        Image portraitImage = portraitGo.AddComponent<Image>();
        portraitImage.raycastTarget = false;
        portraitImage.preserveAspect = true;
        Sprite portrait = Resources.Load<Sprite>("HUD/player_portrait_" + index);
        if (portrait != null)
        {
            portraitImage.sprite = portrait;
            portraitImage.color = Color.white;
        }
        else
        {
            portraitImage.color = new Color(0.18f, 0.2f, 0.22f, 1f); // placeholder swatch (no crash)
        }

        // Name across the bottom of the card.
        TMP_Text name = CreateText(cardGo.transform, CharacterSelection.NameOf(index).ToUpperInvariant(),
            17f, FontStyles.Bold, TextColor);
        name.alignment = TextAlignmentOptions.Center;
        RectTransform nameRect = name.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 0f);
        nameRect.pivot = new Vector2(0.5f, 0f);
        nameRect.sizeDelta = new Vector2(0f, 26f);
        nameRect.anchoredPosition = new Vector2(0f, 10f);

        return button;
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
