using UnityEngine;

// First-person weapon recoil for "School Of The Dead".
//
// This component lives on the spawned weapon MODEL (a child of the camera's
// WeaponHolder). WeaponController spawns that model only for the LOCAL OWNER, so
// everything this script does — including the camera kick added below — is
// inherently local-owner only. Remote players never get a SimpleGunRecoil instance,
// so they never receive camera recoil.
//
// It is VISUAL / FEEL ONLY. It never changes shooting or hit detection:
//   * The weapon model is offset (kick back / up / rotate) and springs back.
//   * The camera gets a small additive kick applied in LateUpdate, layered ON TOP of
//     the mouse-look rotation CoDCamera writes every Update. The firing raycast in
//     WeaponController.Fire() runs in Update and reads the clean mouse-look base, so
//     the shot still goes exactly where the crosshair points. Camera recoil fully
//     decays back to zero, so aim never permanently drifts.
//
// WeaponController only ever touches `muzzleFlash` and `Kick()` — those are kept
// exactly as before so no call sites need to change. Tune the serialized fields per
// weapon prefab for different feel (M1911 vs Uzi vs shotgun, etc.). Guns without this
// component simply get no recoil; nothing is required of every prefab.
public class SimpleGunRecoil : MonoBehaviour
{
    [Header("Camera Recoil (local owner only, visual)")]
    [Tooltip("Upward camera pitch kick per shot, in degrees. Small = subtle climb.")]
    public float cameraKickPitch = 0.6f;
    [Tooltip("Base sideways (yaw) camera kick per shot, in degrees. Usually tiny; most " +
             "horizontal movement should come from randomYawAmount.")]
    public float cameraKickYaw = 0.05f;
    [Tooltip("How fast the camera recoil relaxes back to zero. Higher = snappier recovery.")]
    public float cameraRecoverySpeed = 8f;
    [Tooltip("Random left/right camera yaw kick per shot, in degrees (+/-). Gives auto " +
             "weapons their horizontal jitter.")]
    public float randomYawAmount = 0.25f;
    [Tooltip("Hard cap on accumulated camera recoil (degrees) so rapid fire can never send " +
             "the view flying. Keep modest so the player doesn't fight the camera.")]
    public float maxCameraRecoil = 4f;

    [Header("Weapon Model Kick (visual)")]
    [Tooltip("How far the weapon model kicks backward per shot, in local metres (local -Z).")]
    public float weaponKickBack = 0.05f;
    [Tooltip("How far the weapon model rises per shot, in local metres (local +Y).")]
    public float weaponKickUp = 0.015f;
    [Tooltip("Rotational kick of the weapon model per shot, in degrees (muzzle tilts up).")]
    public float weaponRotationKick = 4f;
    [Tooltip("How fast the weapon model springs back to its rest pose. Higher = snappier.")]
    public float weaponRecoverySpeed = 10f;
    [Tooltip("Hard cap on accumulated weapon positional kick, in local metres.")]
    public float maxWeaponRecoil = 0.12f;

    [Header("Feel")]
    [Tooltip("How quickly the visible recoil chases its target (kick snappiness). Higher = " +
             "instant/punchy, lower = softer.")]
    public float recoilSmoothing = 20f;

    [Header("Shot Effects")]
    [Tooltip("Muzzle flash particle. Bound at runtime by WeaponController per weapon.")]
    public ParticleSystem muzzleFlash;
    [Tooltip("Optional AudioSource used to play the gunshot.")]
    public AudioSource gunAudio;
    [Tooltip("Optional gunshot clip played on each shot.")]
    public AudioClip gunshotSound;

    [Header("Debug")]
    [Tooltip("Log recoil values when firing (throttled so it never spams the console). " +
             "Leave off in normal play.")]
    public bool debugRecoil = false;

    // --- Weapon model rest pose (captured after WeaponController positions the model) ---
    private Vector3 startPosition;
    private Quaternion startRotation;

    // Weapon model recoil state: a "target" that we push on each shot and that relaxes to
    // rest, plus a "current" that smoothly chases the target for a snappy-then-smooth feel.
    private Vector3 weaponPosTarget;
    private Vector3 weaponPosCurrent;
    private Vector3 weaponRotTarget;   // euler degrees offset from rest
    private Vector3 weaponRotCurrent;

    // --- Camera recoil ---
    private Transform cameraTransform;          // resolved from parent chain; null = no cam kick
    private Vector2 camRecoilTarget;            // x = pitch up, y = yaw
    private Vector2 camRecoilCurrent;
    // Drift protection: remember what we wrote and the base we wrote it onto. If nothing
    // else moved the camera since (e.g. CoDCamera stops writing while the cursor is
    // unlocked), reuse the stored base so our additive kick can never accumulate.
    private Quaternion lastCamBase = Quaternion.identity;
    private Quaternion lastCamWritten = Quaternion.identity;
    private bool hasWrittenCam;

    private float nextDebugLogTime;

    private void Start()
    {
        // Captured in Start (not Awake): WeaponController positions the freshly spawned
        // model right after Instantiate, before the first Update, so this rest pose matches
        // where the model was placed.
        startPosition = transform.localPosition;
        startRotation = transform.localRotation;

        // The model sits under WeaponHolder -> Camera, so the owning first-person camera is
        // up the parent chain. If there is none (e.g. a gun placed in the world), camera
        // recoil is simply skipped and only the weapon-model kick runs.
        Camera parentCamera = GetComponentInParent<Camera>();
        cameraTransform = parentCamera != null ? parentCamera.transform : null;

        ResetRecoil();
    }

    // Frame-rate independent smoothing factor for a "speed" (approaches target with a
    // stable exponential response, never overshoots even at low FPS). No allocation.
    private static float SmoothFactor(float speed, float deltaTime)
    {
        if (speed <= 0f)
        {
            return 1f; // snap instantly if disabled
        }
        return 1f - Mathf.Exp(-speed * deltaTime);
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return; // paused (timeScale 0): hold pose, don't integrate
        }

        IntegrateWeaponRecoil(dt);
        IntegrateCameraRecoil(dt);
    }

    private void IntegrateWeaponRecoil(float dt)
    {
        float recover = SmoothFactor(Mathf.Max(0f, weaponRecoverySpeed), dt);
        float chase = SmoothFactor(Mathf.Max(0f, recoilSmoothing), dt);

        // Targets relax back toward the rest pose (recovery); the visible offset chases
        // the target (snappy kick).
        weaponPosTarget = Vector3.Lerp(weaponPosTarget, Vector3.zero, recover);
        weaponRotTarget = Vector3.Lerp(weaponRotTarget, Vector3.zero, recover);
        weaponPosCurrent = Vector3.Lerp(weaponPosCurrent, weaponPosTarget, chase);
        weaponRotCurrent = Vector3.Lerp(weaponRotCurrent, weaponRotTarget, chase);

        transform.localPosition = startPosition + weaponPosCurrent;
        transform.localRotation = startRotation * Quaternion.Euler(weaponRotCurrent);
    }

    private void IntegrateCameraRecoil(float dt)
    {
        float recover = SmoothFactor(Mathf.Max(0f, cameraRecoverySpeed), dt);
        float chase = SmoothFactor(Mathf.Max(0f, recoilSmoothing), dt);

        camRecoilTarget = Vector2.Lerp(camRecoilTarget, Vector2.zero, recover);
        camRecoilCurrent = Vector2.Lerp(camRecoilCurrent, camRecoilTarget, chase);

        if (cameraTransform == null)
        {
            return;
        }

        // Determine the mouse-look base to layer recoil onto. CoDCamera overwrites the
        // camera's localRotation every Update while the cursor is locked, so normally the
        // current value IS a clean base. But while the cursor is unlocked it stops writing;
        // in that case reuse the base we last wrote onto so the additive kick never stacks.
        Quaternion baseRotation;
        if (hasWrittenCam && Approximately(cameraTransform.localRotation, lastCamWritten))
        {
            baseRotation = lastCamBase;
        }
        else
        {
            baseRotation = cameraTransform.localRotation;
        }

        // Negative pitch euler tilts the view UP (matches CoDCamera's convention). Applied
        // in the camera's local space so it reads as an additive look offset.
        Quaternion result = baseRotation * Quaternion.Euler(-camRecoilCurrent.x, camRecoilCurrent.y, 0f);
        cameraTransform.localRotation = result;

        lastCamBase = baseRotation;
        lastCamWritten = result;
        hasWrittenCam = true;
    }

    private static bool Approximately(Quaternion a, Quaternion b)
    {
        // Cheap orientation-equality test (no Acos/allocation). ~identical when |dot| ~ 1.
        return Mathf.Abs(Quaternion.Dot(a, b)) > 0.99999f;
    }

    // Called once per shot by WeaponController (local owner only). Adds a clamped kick to
    // the weapon model and camera, and plays muzzle flash + sound. Repeated calls on auto
    // fire accumulate up to the max clamps, then hold — giving controllable recoil climb.
    public void Kick()
    {
        // --- Weapon model kick: back + up, plus a rotational muzzle tilt. ---
        weaponPosTarget += new Vector3(0f, weaponKickUp, -weaponKickBack);
        weaponPosTarget = Vector3.ClampMagnitude(weaponPosTarget, Mathf.Max(0f, maxWeaponRecoil));

        float rotClamp = Mathf.Max(0f, maxCameraRecoil) + Mathf.Max(0f, weaponRotationKick);
        weaponRotTarget.x = Mathf.Clamp(weaponRotTarget.x - weaponRotationKick, -rotClamp, rotClamp);

        // --- Camera kick: upward pitch + small random horizontal jitter, all clamped. ---
        float maxCam = Mathf.Max(0f, maxCameraRecoil);
        camRecoilTarget.x = Mathf.Clamp(camRecoilTarget.x + cameraKickPitch, -maxCam, maxCam);

        float yaw = cameraKickYaw + Random.Range(-randomYawAmount, randomYawAmount);
        camRecoilTarget.y = Mathf.Clamp(camRecoilTarget.y + yaw, -maxCam, maxCam);

        if (muzzleFlash != null)
        {
            muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            muzzleFlash.Play();
        }

        if (gunAudio != null && gunshotSound != null)
        {
            gunAudio.PlayOneShot(gunshotSound);
        }

        if (debugRecoil && Time.unscaledTime >= nextDebugLogTime)
        {
            // Throttled so full-auto fire can't spam the console.
            nextDebugLogTime = Time.unscaledTime + 0.25f;
            Debug.Log("[SimpleGunRecoil] Kick cam=" + camRecoilTarget + " weaponPos=" +
                      weaponPosTarget + " weaponRot=" + weaponRotTarget);
        }
    }

    /// <summary>
    /// Clear all recoil immediately and snap the weapon model back to its rest pose.
    /// Safe to call any time (weapon switch, reload, death, etc.). Cheap, no allocation.
    /// </summary>
    public void ResetRecoil()
    {
        weaponPosTarget = Vector3.zero;
        weaponPosCurrent = Vector3.zero;
        weaponRotTarget = Vector3.zero;
        weaponRotCurrent = Vector3.zero;
        camRecoilTarget = Vector2.zero;
        camRecoilCurrent = Vector2.zero;
        hasWrittenCam = false;

        transform.localPosition = startPosition;
        transform.localRotation = startRotation;
    }

    private void OnDisable()
    {
        // Leave the model at rest and drop camera-recoil bookkeeping so a re-enable starts
        // clean. CoDCamera restores the camera to the mouse-look base on its next Update.
        ResetRecoil();
    }
}
