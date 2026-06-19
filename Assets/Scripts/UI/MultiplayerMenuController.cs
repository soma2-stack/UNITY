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
    private Action backAction;
    private MultiplayerSessionController session;

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
        // Full-screen dim backdrop so the card reads cleanly over the menu video.
        gameObject.AddComponent<Image>().color = BackgroundColor;

        // Card: vertically stretched with margins so it always fits the screen height.
        GameObject card = CreateUiObject("Card", transform);
        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0f);
        cardRect.anchorMax = new Vector2(0.5f, 1f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(840f, -70f); // width 840; height = screen - 70
        cardRect.anchoredPosition = Vector2.zero;
        card.AddComponent<Image>().color = new Color(0.07f, 0.075f, 0.085f, 0.99f);

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
        backRect.anchorMin = Vector2.zero;
        backRect.anchorMax = Vector2.one;
        backRect.offsetMin = new Vector2(18f, 14f);
        backRect.offsetMax = new Vector2(-18f, -12f);

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

        GameObject content = CreateUiObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
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

        statusText.text = state.ToString().ToUpperInvariant();
        statusText.color = state == MultiplayerSessionState.Failed ? ErrorColor : WarmColor;
        hostButton.interactable = offline && !busy;
        joinButton.interactable = offline && !busy;
        displayNameInput.interactable = offline && !busy;
        joinCodeInput.interactable = offline && !busy;
        startButton.gameObject.SetActive(inLobby && session != null && session.IsHost);
        copyButton.gameObject.SetActive(inLobby && session != null && session.IsHost);
        leaveButton.gameObject.SetActive(inLobby);
        reconnectButton.gameObject.SetActive(state == MultiplayerSessionState.Failed && session != null && session.CanReconnect);
        backButton.interactable = !busy;
    }

    private void HandleRosterChanged(System.Collections.Generic.IReadOnlyList<RosterEntry> entries)
    {
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
        input.onValueChanged.AddListener(value =>
        {
            if (uppercase && value != value.ToUpperInvariant())
            {
                input.SetTextWithoutNotify(value.ToUpperInvariant());
            }
        });

        TMP_Text text = CreateText(root.transform, string.Empty, 20f, FontStyles.Normal, TextColor);
        Stretch(text.rectTransform);
        text.margin = new Vector4(18f, 10f, 18f, 10f);
        input.textComponent = text;

        TMP_Text placeholder = CreateText(root.transform, placeholderValue, 20f, FontStyles.Normal, new Color(1f, 1f, 1f, 0.35f));
        Stretch(placeholder.rectTransform);
        placeholder.margin = new Vector4(18f, 10f, 18f, 10f);
        input.placeholder = placeholder;
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
