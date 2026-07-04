using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent, runtime-applied settings singleton for School Of The Dead.
///
/// Self-bootstraps before the first scene loads (RuntimeInitializeOnLoadMethod) so it
/// is available from the main menu (no player in scene) and from gameplay alike.
///
/// All settings persist via PlayerPrefs (loaded on bootstrap, saved on each Set).
/// Every change fires <see cref="OnSettingsChanged"/> so UI and audio can react.
///
/// AUDIO NOTE: This project currently has no AudioMixer. MasterVolume is applied
/// directly to AudioListener.volume. MusicVolume and SfxVolume are STORED and exposed
/// as public getters; individual AudioSources should read <see cref="MusicVolume"/> /
/// <see cref="SfxVolume"/> (and listen to <see cref="OnSettingsChanged"/>) to apply them.
/// If an AudioMixer is added later, route these to exposed mixer parameters in ApplyAudio().
///
/// CAMERA NOTE: Two camera scripts coexist. PlayerMovement.mouseSensitivity operates on a
/// small scale (~2). CoDCamera.mouseSensitivity operates on a large scale (~200). The
/// stored MouseSensitivity (0.5..10, default 2) is applied raw to PlayerMovement and
/// scaled by CODCAMERA_SENS_SCALE (100) for CoDCamera so both feel consistent.
/// FieldOfView is applied to Camera.main and to CoDCamera.defaultFOV (its ADS logic drives
/// Camera.fieldOfView from defaultFOV, so setting defaultFOV is what actually sticks).
/// </summary>
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    /// <summary>Raised after any setting changes (and after a load/reset).</summary>
    public static event System.Action OnSettingsChanged;

    // ---- PlayerPrefs keys ----
    private const string KEY_MASTER = "sotd_master_volume";
    private const string KEY_MUSIC = "sotd_music_volume";
    private const string KEY_SFX = "sotd_sfx_volume";
    private const string KEY_SENS = "sotd_mouse_sensitivity";
    private const string KEY_INVERT = "sotd_invert_mouse_y";
    private const string KEY_FOV = "sotd_field_of_view";
    private const string KEY_QUALITY = "sotd_quality_level";
    private const string KEY_FULLSCREEN = "sotd_fullscreen";
    private const string KEY_VSYNC = "sotd_vsync";
    private const string KEY_FPS = "sotd_fps_cap";
    private const string KEY_RES = "sotd_resolution_index";

    // ---- Ranges / defaults ----
    public const float MASTER_DEFAULT = 1f;
    public const float MUSIC_DEFAULT = 1f;
    public const float SFX_DEFAULT = 1f;

    public const float SENS_MIN = 0.5f;
    public const float SENS_MAX = 10f;
    public const float SENS_DEFAULT = 2f;

    public const float FOV_MIN = 60f;
    public const float FOV_MAX = 110f;
    public const float FOV_DEFAULT = 90f;

    public const bool INVERT_DEFAULT = false;
    public const bool FULLSCREEN_DEFAULT = true;
    public const bool VSYNC_DEFAULT = true;

    // FPS cap options for the UI. 0 == Unlimited. Note: a cap only takes effect when VSync is
    // OFF (Unity ignores Application.targetFrameRate while vSyncCount > 0).
    public static readonly int[] FPS_OPTIONS = { 0, 30, 60, 120, 144 };
    public const int FPS_DEFAULT = 0; // Unlimited

    /// <summary>Multiplier converting the stored sensitivity (~2) to CoDCamera's scale (~200).</summary>
    private const float CODCAMERA_SENS_SCALE = 100f;

    // ---- Backing fields ----
    private float _masterVolume = MASTER_DEFAULT;
    private float _musicVolume = MUSIC_DEFAULT;
    private float _sfxVolume = SFX_DEFAULT;
    private float _mouseSensitivity = SENS_DEFAULT;
    private bool _invertMouseY = INVERT_DEFAULT;
    private float _fieldOfView = FOV_DEFAULT;
    private int _qualityLevel = 0;
    private bool _fullscreen = FULLSCREEN_DEFAULT;
    private bool _vSync = VSYNC_DEFAULT;
    private int _fpsCap = FPS_DEFAULT;
    private int _resolutionIndex = -1; // resolved on load against Screen.resolutions

    // ---- Public getters ----
    public float MasterVolume => _masterVolume;
    public float MusicVolume => _musicVolume;
    public float SfxVolume => _sfxVolume;
    public float MouseSensitivity => _mouseSensitivity;
    public bool InvertMouseY => _invertMouseY;
    public float FieldOfView => _fieldOfView;
    public int QualityLevel => _qualityLevel;
    public bool Fullscreen => _fullscreen;
    public bool VSync => _vSync;
    public int FpsCap => _fpsCap;
    public int ResolutionIndex => _resolutionIndex;
    /// <summary>Available fullscreen resolutions (may be empty on some platforms).</summary>
    public Resolution[] AvailableResolutions => Screen.resolutions;

    // ---------------------------------------------------------------------
    // Bootstrap
    // ---------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
        {
            return;
        }

        var go = new GameObject("SettingsManager");
        Instance = go.AddComponent<SettingsManager>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Load();
        ApplyAll();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The player and main camera only exist in gameplay; re-apply the settings that
        // depend on those objects each time a scene loads. All apply methods are null-safe.
        ApplyCamera();
        ApplyAudio();
        ApplyQuality();
        ApplyDisplay();
    }

    // ---------------------------------------------------------------------
    // Load / Save
    // ---------------------------------------------------------------------

    private void Load()
    {
        _masterVolume = PlayerPrefs.GetFloat(KEY_MASTER, MASTER_DEFAULT);
        _musicVolume = PlayerPrefs.GetFloat(KEY_MUSIC, MUSIC_DEFAULT);
        _sfxVolume = PlayerPrefs.GetFloat(KEY_SFX, SFX_DEFAULT);
        _mouseSensitivity = PlayerPrefs.GetFloat(KEY_SENS, SENS_DEFAULT);
        _invertMouseY = PlayerPrefs.GetInt(KEY_INVERT, INVERT_DEFAULT ? 1 : 0) == 1;
        _fieldOfView = PlayerPrefs.GetFloat(KEY_FOV, FOV_DEFAULT);
        _qualityLevel = PlayerPrefs.GetInt(KEY_QUALITY, GetDefaultQualityLevel());
        _fullscreen = PlayerPrefs.GetInt(KEY_FULLSCREEN, FULLSCREEN_DEFAULT ? 1 : 0) == 1;
        _vSync = PlayerPrefs.GetInt(KEY_VSYNC, VSYNC_DEFAULT ? 1 : 0) == 1;
        _fpsCap = PlayerPrefs.GetInt(KEY_FPS, FPS_DEFAULT);
        _resolutionIndex = PlayerPrefs.GetInt(KEY_RES, GetDefaultResolutionIndex());

        Clamp();
    }

    private void Save()
    {
        PlayerPrefs.SetFloat(KEY_MASTER, _masterVolume);
        PlayerPrefs.SetFloat(KEY_MUSIC, _musicVolume);
        PlayerPrefs.SetFloat(KEY_SFX, _sfxVolume);
        PlayerPrefs.SetFloat(KEY_SENS, _mouseSensitivity);
        PlayerPrefs.SetInt(KEY_INVERT, _invertMouseY ? 1 : 0);
        PlayerPrefs.SetFloat(KEY_FOV, _fieldOfView);
        PlayerPrefs.SetInt(KEY_QUALITY, _qualityLevel);
        PlayerPrefs.SetInt(KEY_FULLSCREEN, _fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(KEY_VSYNC, _vSync ? 1 : 0);
        PlayerPrefs.SetInt(KEY_FPS, _fpsCap);
        PlayerPrefs.SetInt(KEY_RES, _resolutionIndex);
        PlayerPrefs.Save();
    }

    private void Clamp()
    {
        _masterVolume = Mathf.Clamp01(_masterVolume);
        _musicVolume = Mathf.Clamp01(_musicVolume);
        _sfxVolume = Mathf.Clamp01(_sfxVolume);
        _mouseSensitivity = Mathf.Clamp(_mouseSensitivity, SENS_MIN, SENS_MAX);
        _fieldOfView = Mathf.Clamp(_fieldOfView, FOV_MIN, FOV_MAX);

        int qualityCount = QualitySettings.names != null ? QualitySettings.names.Length : 0;
        if (qualityCount > 0)
        {
            _qualityLevel = Mathf.Clamp(_qualityLevel, 0, qualityCount - 1);
        }
        else
        {
            _qualityLevel = 0;
        }

        if (_fpsCap < 0)
        {
            _fpsCap = 0;
        }

        Resolution[] resolutions = Screen.resolutions;
        if (resolutions != null && resolutions.Length > 0)
        {
            if (_resolutionIndex < 0 || _resolutionIndex >= resolutions.Length)
            {
                _resolutionIndex = GetDefaultResolutionIndex();
            }
        }
        else
        {
            _resolutionIndex = -1;
        }
    }

    private static int GetDefaultQualityLevel()
    {
        return QualitySettings.GetQualityLevel();
    }

    // Index of the current display resolution within Screen.resolutions (or the largest, or -1
    // when the platform reports no resolution list).
    private static int GetDefaultResolutionIndex()
    {
        Resolution[] resolutions = Screen.resolutions;
        if (resolutions == null || resolutions.Length == 0)
        {
            return -1;
        }

        Resolution current = Screen.currentResolution;
        for (int i = 0; i < resolutions.Length; i++)
        {
            if (resolutions[i].width == current.width && resolutions[i].height == current.height)
            {
                return i;
            }
        }
        return resolutions.Length - 1;
    }

    // ---------------------------------------------------------------------
    // Setters (apply + save + notify)
    // ---------------------------------------------------------------------

    public void SetMasterVolume(float value)
    {
        _masterVolume = Mathf.Clamp01(value);
        ApplyAudio();
        Save();
        NotifyChanged();
    }

    public void SetMusicVolume(float value)
    {
        _musicVolume = Mathf.Clamp01(value);
        ApplyAudio();
        Save();
        NotifyChanged();
    }

    public void SetSfxVolume(float value)
    {
        _sfxVolume = Mathf.Clamp01(value);
        ApplyAudio();
        Save();
        NotifyChanged();
    }

    public void SetMouseSensitivity(float value)
    {
        _mouseSensitivity = Mathf.Clamp(value, SENS_MIN, SENS_MAX);
        ApplyCamera();
        Save();
        NotifyChanged();
    }

    public void SetInvertMouseY(bool value)
    {
        _invertMouseY = value;
        ApplyCamera();
        Save();
        NotifyChanged();
    }

    public void SetFieldOfView(float value)
    {
        _fieldOfView = Mathf.Clamp(value, FOV_MIN, FOV_MAX);
        ApplyCamera();
        Save();
        NotifyChanged();
    }

    public void SetQualityLevel(int value)
    {
        int qualityCount = QualitySettings.names != null ? QualitySettings.names.Length : 0;
        if (qualityCount > 0)
        {
            _qualityLevel = Mathf.Clamp(value, 0, qualityCount - 1);
        }
        else
        {
            _qualityLevel = 0;
        }

        ApplyQuality();
        Save();
        NotifyChanged();
    }

    public void SetFullscreen(bool value)
    {
        _fullscreen = value;
        ApplyDisplay();
        Save();
        NotifyChanged();
    }

    public void SetVSync(bool value)
    {
        _vSync = value;
        ApplyDisplay();
        Save();
        NotifyChanged();
    }

    public void SetFpsCap(int value)
    {
        _fpsCap = Mathf.Max(0, value);
        ApplyDisplay();
        Save();
        NotifyChanged();
    }

    public void SetResolutionIndex(int index)
    {
        Resolution[] resolutions = Screen.resolutions;
        if (resolutions != null && resolutions.Length > 0)
        {
            _resolutionIndex = Mathf.Clamp(index, 0, resolutions.Length - 1);
        }
        ApplyDisplay();
        Save();
        NotifyChanged();
    }

    // ---------------------------------------------------------------------
    // Reset
    // ---------------------------------------------------------------------

    /// <summary>Re-apply and persist all settings (used by the menu's APPLY button).</summary>
    public void ApplyAndSave()
    {
        ApplyAll();
        Save();
        NotifyChanged();
    }

    public void ResetToDefaults()
    {
        _masterVolume = MASTER_DEFAULT;
        _musicVolume = MUSIC_DEFAULT;
        _sfxVolume = SFX_DEFAULT;
        _mouseSensitivity = SENS_DEFAULT;
        _invertMouseY = INVERT_DEFAULT;
        _fieldOfView = FOV_DEFAULT;
        _qualityLevel = GetDefaultQualityLevel();
        _fullscreen = FULLSCREEN_DEFAULT;
        _vSync = VSYNC_DEFAULT;
        _fpsCap = FPS_DEFAULT;
        _resolutionIndex = GetDefaultResolutionIndex();

        Clamp();
        ApplyAll();
        Save();
        NotifyChanged();
    }

    // ---------------------------------------------------------------------
    // Apply
    // ---------------------------------------------------------------------

    private void ApplyAll()
    {
        ApplyAudio();
        ApplyCamera();
        ApplyQuality();
        ApplyDisplay();
    }

    private void ApplyAudio()
    {
        // Master maps directly to the global listener volume.
        AudioListener.volume = _masterVolume;
        // Music / SFX have no AudioMixer to route to; they are stored and exposed via
        // MusicVolume / SfxVolume + OnSettingsChanged for AudioSources to read. (See class doc.)
    }

    private void ApplyCamera()
    {
        // PlayerMovement: native small-scale sensitivity. Null-safe (no player in menu).
        var movement = LocalPlayer.Movement;
        if (movement != null)
        {
            movement.mouseSensitivity = _mouseSensitivity;
        }

        // CoDCamera: large-scale sensitivity + invert + FOV (via defaultFOV).
        var codCamera = LocalPlayer.Camera;
        if (codCamera != null)
        {
            codCamera.mouseSensitivity = _mouseSensitivity * CODCAMERA_SENS_SCALE;
            codCamera.invertY = _invertMouseY;
            codCamera.defaultFOV = _fieldOfView;
        }

        // Main camera FOV (guarded). If a CoDCamera is present its ADS lerp will keep this
        // in sync from defaultFOV; otherwise we set it directly here.
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.fieldOfView = _fieldOfView;
        }
    }

    private void ApplyQuality()
    {
        int qualityCount = QualitySettings.names != null ? QualitySettings.names.Length : 0;
        if (qualityCount > 0)
        {
            int level = Mathf.Clamp(_qualityLevel, 0, qualityCount - 1);
            QualitySettings.SetQualityLevel(level, true);
        }
    }

    private void ApplyDisplay()
    {
        // Apply the chosen resolution + fullscreen mode when the platform exposes a resolution
        // list; otherwise just set the fullscreen flag. The bool overload of SetResolution keeps
        // the current refresh rate and is version-safe across Unity releases.
        Resolution[] resolutions = Screen.resolutions;
        if (resolutions != null && resolutions.Length > 0 &&
            _resolutionIndex >= 0 && _resolutionIndex < resolutions.Length)
        {
            Resolution resolution = resolutions[_resolutionIndex];
            if (Screen.width != resolution.width || Screen.height != resolution.height ||
                Screen.fullScreen != _fullscreen)
            {
                Screen.SetResolution(resolution.width, resolution.height, _fullscreen);
            }
        }
        else
        {
            Screen.fullScreen = _fullscreen;
        }

        QualitySettings.vSyncCount = _vSync ? 1 : 0;
        // Only meaningful when VSync is off; Unity ignores the cap while vSyncCount > 0.
        Application.targetFrameRate = _fpsCap <= 0 ? -1 : _fpsCap;
    }

    private static void NotifyChanged()
    {
        OnSettingsChanged?.Invoke();
    }
}
