using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SchoolOfTheDead.Settings
{
    /// <summary>
    /// Programmatic uGUI + TextMeshPro settings overlay for School Of The Dead.
    ///
    /// Mirrors the MultiplayerMenuController contract:
    ///   public static GameObject Create(Transform parent, Action onBack)
    /// Builds the overlay under <paramref name="parent"/>, returns the root GameObject,
    /// and starts it INACTIVE. The BACK button invokes <paramref name="onBack"/>.
    ///
    /// All controls bind live to <see cref="SettingsManager"/>. Controls are (re)initialised
    /// from current values whenever the overlay is enabled (OnEnable). Fully null-safe: opening
    /// from the main menu (no player / no Camera.main) never throws.
    /// </summary>
    public class SettingsMenuController : MonoBehaviour
    {
        // ---- Palette (matches the menu aesthetic) ----
        private static readonly Color ColorBackground = new Color(0.04f, 0.04f, 0.05f, 0.96f);
        private static readonly Color ColorPanel = new Color(0.08f, 0.08f, 0.09f, 0.98f);
        private static readonly Color ColorAccentRed = new Color(0.79f, 0.13f, 0.08f, 1f);
        private static readonly Color ColorAmber = new Color(0.95f, 0.65f, 0.20f, 1f);
        private static readonly Color ColorTextOffWhite = new Color(0.92f, 0.90f, 0.86f, 1f);
        private static readonly Color ColorTextDim = new Color(0.62f, 0.60f, 0.57f, 1f);
        private static readonly Color ColorSliderTrack = new Color(0.16f, 0.16f, 0.18f, 1f);
        private static readonly Color ColorToggleOff = new Color(0.16f, 0.16f, 0.18f, 1f);
        private static readonly Color ColorButton = new Color(0.13f, 0.13f, 0.15f, 1f);
        private static readonly Color ColorButtonHover = new Color(0.20f, 0.20f, 0.23f, 1f);

        private const int RowFontSize = 26;
        private const int ValueFontSize = 24;
        private const int HeaderFontSize = 54;

        private Action _onBack;
        private bool _suppressCallbacks;

        // Control references (rebuilt once, refreshed on enable).
        private Slider _masterSlider;
        private TMP_Text _masterValue;
        private Slider _musicSlider;
        private TMP_Text _musicValue;
        private Slider _sfxSlider;
        private TMP_Text _sfxValue;
        private Slider _sensSlider;
        private TMP_Text _sensValue;
        private Slider _fovSlider;
        private TMP_Text _fovValue;

        private Toggle _invertToggle;
        private Toggle _fullscreenToggle;
        private Toggle _vsyncToggle;

        private TMP_Text _qualityValue;
        private int _qualityIndex;
        private string[] _qualityNames;

        // ---------------------------------------------------------------------
        // Public contract
        // ---------------------------------------------------------------------

        public static GameObject Create(Transform parent, Action onBack)
        {
            var root = new GameObject("SettingsMenu", typeof(RectTransform));
            root.transform.SetParent(parent, false);

            var controller = root.AddComponent<SettingsMenuController>();
            controller._onBack = onBack;
            controller.Build(root);

            root.SetActive(false);
            return root;
        }

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        private void OnEnable()
        {
            RefreshFromSettings();
        }

        // ---------------------------------------------------------------------
        // Build
        // ---------------------------------------------------------------------

        private void Build(GameObject root)
        {
            var rootRect = root.GetComponent<RectTransform>();
            Stretch(rootRect);

            EnsureEventSystem();

            // Full-screen dim background that also blocks clicks behind it.
            var bg = CreateImage("Background", root.transform, ColorBackground);
            Stretch(bg.rectTransform);

            // Centered panel.
            var panel = CreateImage("Panel", root.transform, ColorPanel);
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760f, 820f);

            // Title.
            var title = CreateText("Title", panel.transform, "SETTINGS", HeaderFontSize, ColorTextOffWhite, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -28f);
            titleRect.sizeDelta = new Vector2(-80f, 70f);

            // Accent underline.
            var accent = CreateImage("TitleAccent", panel.transform, ColorAccentRed);
            var accentRect = accent.rectTransform;
            accentRect.anchorMin = new Vector2(0.5f, 1f);
            accentRect.anchorMax = new Vector2(0.5f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.anchoredPosition = new Vector2(0f, -100f);
            accentRect.sizeDelta = new Vector2(120f, 4f);

            // Scrollable content area (rows live in a vertical layout).
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(panel.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 0f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.offsetMin = new Vector2(40f, 110f);
            contentRect.offsetMax = new Vector2(-40f, -130f);

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // ---- AUDIO ----
            CreateSectionLabel(content.transform, "AUDIO");
            (_masterSlider, _masterValue) = CreateSliderRow(content.transform, "Master Volume", 0f, 1f, false);
            (_musicSlider, _musicValue) = CreateSliderRow(content.transform, "Music Volume", 0f, 1f, false);
            (_sfxSlider, _sfxValue) = CreateSliderRow(content.transform, "SFX Volume", 0f, 1f, false);

            // ---- CONTROLS ----
            CreateSectionLabel(content.transform, "CONTROLS");
            (_sensSlider, _sensValue) = CreateSliderRow(content.transform, "Mouse Sensitivity", SettingsManager.SENS_MIN, SettingsManager.SENS_MAX, true);
            (_fovSlider, _fovValue) = CreateSliderRow(content.transform, "Field of View", SettingsManager.FOV_MIN, SettingsManager.FOV_MAX, true);
            _invertToggle = CreateToggleRow(content.transform, "Invert Mouse Y");

            // ---- VIDEO ----
            CreateSectionLabel(content.transform, "VIDEO");
            _qualityValue = CreateQualityRow(content.transform, "Quality");
            _fullscreenToggle = CreateToggleRow(content.transform, "Fullscreen");
            _vsyncToggle = CreateToggleRow(content.transform, "VSync");

            // ---- Footer buttons ----
            var footer = new GameObject("Footer", typeof(RectTransform));
            footer.transform.SetParent(panel.transform, false);
            var footerRect = footer.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = new Vector2(0f, 28f);
            footerRect.sizeDelta = new Vector2(-80f, 56f);

            var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 24f;
            footerLayout.childAlignment = TextAnchor.MiddleCenter;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childForceExpandHeight = true;

            CreateButton(footer.transform, "RESET TO DEFAULTS", OnResetClicked, ColorAmber);
            CreateButton(footer.transform, "BACK", OnBackClicked, ColorAccentRed);
        }

        // ---------------------------------------------------------------------
        // Rows
        // ---------------------------------------------------------------------

        private void CreateSectionLabel(Transform parent, string text)
        {
            var label = CreateText("Section_" + text, parent, text, 22, ColorAccentRed, TextAlignmentOptions.Left);
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = 6f;
            AddRowLayout(label.gameObject, 34f);
        }

        private (Slider, TMP_Text) CreateSliderRow(Transform parent, string label, float min, float max, bool wholeStepDisplay)
        {
            var row = CreateRow(parent, out var labelText, label);

            // Value label (right aligned).
            var value = CreateText("Value", row.transform, "0", ValueFontSize, ColorAmber, TextAlignmentOptions.Right);
            var valueRect = value.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(90f, 0f);
            valueRect.anchoredPosition = new Vector2(0f, 0f);

            // Slider sits between label and value.
            var sliderGo = new GameObject("Slider", typeof(RectTransform));
            sliderGo.transform.SetParent(row.transform, false);
            var sliderRect = sliderGo.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0.40f, 0.5f);
            sliderRect.anchorMax = new Vector2(0.40f, 0.5f);
            sliderRect.pivot = new Vector2(0f, 0.5f);
            sliderRect.sizeDelta = new Vector2(280f, 14f);
            sliderRect.anchoredPosition = new Vector2(0f, 0f);

            var bg = CreateImage("Track", sliderGo.transform, ColorSliderTrack);
            Stretch(bg.rectTransform);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            Stretch(fillAreaRect);

            var fill = CreateImage("Fill", fillArea.transform, ColorAccentRed);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.sizeDelta = new Vector2(10f, 0f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGo.transform, false);
            var handleAreaRect = handleArea.GetComponent<RectTransform>();
            Stretch(handleAreaRect);

            var handle = CreateImage("Handle", handleArea.transform, ColorTextOffWhite);
            var handleRect = handle.rectTransform;
            handleRect.sizeDelta = new Vector2(16f, 26f);

            var slider = sliderGo.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;

            AddRowLayout(row, 44f);
            return (slider, value);
        }

        private Toggle CreateToggleRow(Transform parent, string label)
        {
            var row = CreateRow(parent, out var labelText, label);

            var toggleGo = new GameObject("Toggle", typeof(RectTransform));
            toggleGo.transform.SetParent(row.transform, false);
            var toggleRect = toggleGo.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(1f, 0.5f);
            toggleRect.anchorMax = new Vector2(1f, 0.5f);
            toggleRect.pivot = new Vector2(1f, 0.5f);
            toggleRect.sizeDelta = new Vector2(58f, 30f);
            toggleRect.anchoredPosition = new Vector2(0f, 0f);

            var bg = CreateImage("Background", toggleGo.transform, ColorToggleOff);
            Stretch(bg.rectTransform);

            var check = CreateImage("Checkmark", bg.transform, ColorAccentRed);
            var checkRect = check.rectTransform;
            checkRect.anchorMin = new Vector2(0f, 0f);
            checkRect.anchorMax = new Vector2(1f, 1f);
            checkRect.offsetMin = new Vector2(5f, 5f);
            checkRect.offsetMax = new Vector2(-5f, -5f);

            var toggle = toggleGo.AddComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = check;

            AddRowLayout(row, 44f);
            return toggle;
        }

        private TMP_Text CreateQualityRow(Transform parent, string label)
        {
            var row = CreateRow(parent, out var labelText, label);

            // Right arrow.
            var rightBtn = CreateArrowButton(row.transform, ">", () => StepQuality(1));
            var rightRect = rightBtn.GetComponent<RectTransform>();
            rightRect.anchorMin = new Vector2(1f, 0.5f);
            rightRect.anchorMax = new Vector2(1f, 0.5f);
            rightRect.pivot = new Vector2(1f, 0.5f);
            rightRect.anchoredPosition = new Vector2(0f, 0f);
            rightRect.sizeDelta = new Vector2(40f, 36f);

            // Value text.
            var value = CreateText("Value", row.transform, "-", ValueFontSize, ColorAmber, TextAlignmentOptions.Center);
            var valueRect = value.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 0.5f);
            valueRect.anchorMax = new Vector2(1f, 0.5f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.anchoredPosition = new Vector2(-48f, 0f);
            valueRect.sizeDelta = new Vector2(180f, 36f);

            // Left arrow.
            var leftBtn = CreateArrowButton(row.transform, "<", () => StepQuality(-1));
            var leftRect = leftBtn.GetComponent<RectTransform>();
            leftRect.anchorMin = new Vector2(1f, 0.5f);
            leftRect.anchorMax = new Vector2(1f, 0.5f);
            leftRect.pivot = new Vector2(1f, 0.5f);
            leftRect.anchoredPosition = new Vector2(-236f, 0f);
            leftRect.sizeDelta = new Vector2(40f, 36f);

            AddRowLayout(row, 44f);
            return value;
        }

        private GameObject CreateRow(Transform parent, out TMP_Text labelText, string label)
        {
            var row = new GameObject("Row_" + label, typeof(RectTransform));
            row.transform.SetParent(parent, false);

            labelText = CreateText("Label", row.transform, label, RowFontSize, ColorTextOffWhite, TextAlignmentOptions.Left);
            var labelRect = labelText.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0.40f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return row;
        }

        private void AddRowLayout(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = go.AddComponent<LayoutElement>();
            }
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleWidth = 1f;
        }

        // ---------------------------------------------------------------------
        // Refresh / binding
        // ---------------------------------------------------------------------

        private void RefreshFromSettings()
        {
            var settings = SettingsManager.Instance;
            if (settings == null)
            {
                return;
            }

            _suppressCallbacks = true;

            BindSlider(_masterSlider, settings.MasterVolume, OnMasterChanged);
            BindSlider(_musicSlider, settings.MusicVolume, OnMusicChanged);
            BindSlider(_sfxSlider, settings.SfxVolume, OnSfxChanged);
            BindSlider(_sensSlider, settings.MouseSensitivity, OnSensChanged);
            BindSlider(_fovSlider, settings.FieldOfView, OnFovChanged);

            BindToggle(_invertToggle, settings.InvertMouseY, OnInvertChanged);
            BindToggle(_fullscreenToggle, settings.Fullscreen, OnFullscreenChanged);
            BindToggle(_vsyncToggle, settings.VSync, OnVSyncChanged);

            _qualityNames = QualitySettings.names ?? Array.Empty<string>();
            _qualityIndex = settings.QualityLevel;
            if (_qualityNames.Length > 0)
            {
                _qualityIndex = Mathf.Clamp(_qualityIndex, 0, _qualityNames.Length - 1);
            }

            UpdateMasterLabel();
            UpdateMusicLabel();
            UpdateSfxLabel();
            UpdateSensLabel();
            UpdateFovLabel();
            UpdateQualityLabel();

            _suppressCallbacks = false;
        }

        private void BindSlider(Slider slider, float value, UnityEngine.Events.UnityAction<float> callback)
        {
            if (slider == null)
            {
                return;
            }
            slider.onValueChanged.RemoveAllListeners();
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(callback);
        }

        private void BindToggle(Toggle toggle, bool value, UnityEngine.Events.UnityAction<bool> callback)
        {
            if (toggle == null)
            {
                return;
            }
            toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(value);
            toggle.onValueChanged.AddListener(callback);
        }

        // ---------------------------------------------------------------------
        // Callbacks
        // ---------------------------------------------------------------------

        private void OnMasterChanged(float v)
        {
            if (_suppressCallbacks) return;
            SettingsManager.Instance?.SetMasterVolume(v);
            UpdateMasterLabel();
        }

        private void OnMusicChanged(float v)
        {
            if (_suppressCallbacks) return;
            SettingsManager.Instance?.SetMusicVolume(v);
            UpdateMusicLabel();
        }

        private void OnSfxChanged(float v)
        {
            if (_suppressCallbacks) return;
            SettingsManager.Instance?.SetSfxVolume(v);
            UpdateSfxLabel();
        }

        private void OnSensChanged(float v)
        {
            if (_suppressCallbacks) return;
            SettingsManager.Instance?.SetMouseSensitivity(v);
            UpdateSensLabel();
        }

        private void OnFovChanged(float v)
        {
            if (_suppressCallbacks) return;
            SettingsManager.Instance?.SetFieldOfView(v);
            UpdateFovLabel();
        }

        private void OnInvertChanged(bool v)
        {
            if (_suppressCallbacks) return;
            SettingsManager.Instance?.SetInvertMouseY(v);
        }

        private void OnFullscreenChanged(bool v)
        {
            if (_suppressCallbacks) return;
            SettingsManager.Instance?.SetFullscreen(v);
        }

        private void OnVSyncChanged(bool v)
        {
            if (_suppressCallbacks) return;
            SettingsManager.Instance?.SetVSync(v);
        }

        private void StepQuality(int delta)
        {
            if (_qualityNames == null || _qualityNames.Length == 0)
            {
                return;
            }

            _qualityIndex = Mathf.Clamp(_qualityIndex + delta, 0, _qualityNames.Length - 1);
            SettingsManager.Instance?.SetQualityLevel(_qualityIndex);
            UpdateQualityLabel();
        }

        private void OnResetClicked()
        {
            SettingsManager.Instance?.ResetToDefaults();
            RefreshFromSettings();
        }

        private void OnBackClicked()
        {
            _onBack?.Invoke();
        }

        // ---------------------------------------------------------------------
        // Label updates
        // ---------------------------------------------------------------------

        private void UpdateMasterLabel()
        {
            if (_masterValue != null && _masterSlider != null)
            {
                _masterValue.text = Mathf.RoundToInt(_masterSlider.value * 100f) + "%";
            }
        }

        private void UpdateMusicLabel()
        {
            if (_musicValue != null && _musicSlider != null)
            {
                _musicValue.text = Mathf.RoundToInt(_musicSlider.value * 100f) + "%";
            }
        }

        private void UpdateSfxLabel()
        {
            if (_sfxValue != null && _sfxSlider != null)
            {
                _sfxValue.text = Mathf.RoundToInt(_sfxSlider.value * 100f) + "%";
            }
        }

        private void UpdateSensLabel()
        {
            if (_sensValue != null && _sensSlider != null)
            {
                _sensValue.text = _sensSlider.value.ToString("0.0");
            }
        }

        private void UpdateFovLabel()
        {
            if (_fovValue != null && _fovSlider != null)
            {
                _fovValue.text = Mathf.RoundToInt(_fovSlider.value).ToString();
            }
        }

        private void UpdateQualityLabel()
        {
            if (_qualityValue == null)
            {
                return;
            }

            if (_qualityNames != null && _qualityNames.Length > 0)
            {
                int idx = Mathf.Clamp(_qualityIndex, 0, _qualityNames.Length - 1);
                _qualityValue.text = _qualityNames[idx];
            }
            else
            {
                _qualityValue.text = "-";
            }
        }

        // ---------------------------------------------------------------------
        // UI helpers
        // ---------------------------------------------------------------------

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static TMP_Text CreateText(string name, Transform parent, string content, int fontSize, Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        private GameObject CreateButton(Transform parent, string label, Action onClick, Color accent)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = ColorButton;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = ColorButton;
            colors.highlightedColor = ColorButtonHover;
            colors.pressedColor = accent;
            colors.selectedColor = ColorButtonHover;
            colors.disabledColor = ColorButton;
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var text = CreateText("Text", go.transform, label, RowFontSize, ColorTextOffWhite, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform);

            // Accent strip along the bottom edge.
            var strip = CreateImage("AccentStrip", go.transform, accent);
            var stripRect = strip.rectTransform;
            stripRect.anchorMin = new Vector2(0f, 0f);
            stripRect.anchorMax = new Vector2(1f, 0f);
            stripRect.pivot = new Vector2(0.5f, 0f);
            stripRect.sizeDelta = new Vector2(0f, 3f);
            strip.raycastTarget = false;

            return go;
        }

        private GameObject CreateArrowButton(Transform parent, string symbol, Action onClick)
        {
            var go = new GameObject("Arrow_" + symbol, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = ColorButton;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = ColorButton;
            colors.highlightedColor = ColorButtonHover;
            colors.pressedColor = ColorAccentRed;
            colors.selectedColor = ColorButtonHover;
            colors.disabledColor = ColorButton;
            button.colors = colors;
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var text = CreateText("Text", go.transform, symbol, RowFontSize, ColorAmber, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform);

            return go;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
    }
}
