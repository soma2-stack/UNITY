using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LOCAL, cosmetic-only floating prize model for the Mystery Box. Once a box's pending prize has
/// finished its 2-second name roll (and until it is claimed or expires), each player builds a
/// private, non-interactive copy of the chosen weapon's existing <see cref="Weapon.weaponModel"/>
/// hovering and slowly spinning above the box.
///
/// It reads ONLY the already-synced pending-prize state exposed by <see cref="MysteryBox"/>
/// (HasPendingPrize / PendingWeaponIndex / PendingRevealEndsAt / PendingExpiresAt) and the public
/// weaponPool — it never selects, claims, grants, or networks anything. The visual is deliberately
/// hidden during the roll so it can't reveal the prize early, and is destroyed the instant the
/// pending state clears (claim/expiry) or the box is destroyed.
///
/// Self-attaches at runtime to every MysteryBox in the gameplay scene (no prefab/scene edits, no
/// Inspector setup). If the chosen weapon has no weaponModel, no visual is shown and the box works
/// exactly as before.
/// </summary>
[DisallowMultipleComponent]
public sealed class MysteryBoxPrizeVisual : MonoBehaviour
{
    private const string GameplayScene = "SchoolOfTheDead";

    [Header("Floating Prize Visual (local, cosmetic only)")]
    [Tooltip("Height in meters the model hovers above the box origin.")]
    public float hoverHeight = 1.1f;
    [Tooltip("Slow spin speed in degrees per second.")]
    public float rotationSpeed = 45f;
    [Tooltip("Uniform scale multiplier applied on top of the model's own scale.")]
    public float displayScale = 1f;
    [Tooltip("Gentle vertical bob amplitude in meters (0 = no bob).")]
    public float bobAmplitude = 0.06f;
    [Tooltip("Bob cycles per second.")]
    public float bobSpeed = 2f;

    private MysteryBox box;
    private GameObject currentModel;
    private int currentIndex = -1;
    private float bobPhase;

    // ---- Runtime self-attach (no scene/prefab edits) ---------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnAnySceneLoaded;
        SceneManager.sceneLoaded += OnAnySceneLoaded;
        AttachToBoxes(SceneManager.GetActiveScene());
    }

    private static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AttachToBoxes(scene);
    }

    private static void AttachToBoxes(Scene scene)
    {
        if (scene.name != GameplayScene)
        {
            return;
        }
        MysteryBox[] boxes = FindObjectsByType<MysteryBox>(FindObjectsSortMode.None);
        foreach (MysteryBox b in boxes)
        {
            if (b != null && b.GetComponent<MysteryBoxPrizeVisual>() == null)
            {
                b.gameObject.AddComponent<MysteryBoxPrizeVisual>();
            }
        }
    }

    // ---- Lifecycle --------------------------------------------------------------------------

    private void Awake()
    {
        box = GetComponent<MysteryBox>();
    }

    private void OnDestroy()
    {
        DestroyModel();
    }

    private void Update()
    {
        if (box == null)
        {
            return;
        }

        if (!TryGetDisplayIndex(out int idx))
        {
            DestroyModel();
            return;
        }

        EnsureModel(idx);
        if (currentModel == null)
        {
            return;
        }

        // Slow spin + gentle hover bob.
        currentModel.transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.World);
        bobPhase += Time.deltaTime * Mathf.Max(0f, bobSpeed);
        float bob = Mathf.Sin(bobPhase) * bobAmplitude;
        currentModel.transform.localPosition = new Vector3(0f, hoverHeight + bob, 0f);
    }

    // Show only AFTER the name roll finishes and before the prize expires. HasPendingPrize is
    // already false once the prize is claimed/cleared, so this hides it immediately then.
    private bool TryGetDisplayIndex(out int idx)
    {
        idx = -1;
        if (!box.HasPendingPrize)
        {
            return false;
        }
        double now = NetworkGameplayCoordinator.SharedTime;
        if (now < box.PendingRevealEndsAt || now >= box.PendingExpiresAt)
        {
            return false;
        }
        idx = box.PendingWeaponIndex;
        return idx >= 0;
    }

    private void EnsureModel(int idx)
    {
        if (currentModel != null && currentIndex == idx)
        {
            return; // already showing this prize
        }
        DestroyModel();

        GameObject src = ResolvePrizeModel(idx);
        if (src == null)
        {
            return; // no model on this weapon -> no visual, box still works
        }

        currentModel = Instantiate(src);
        currentIndex = idx;
        currentModel.name = "MysteryBoxPrizeVisual (Runtime)";
        MakeVisualOnly(currentModel);

        Transform t = currentModel.transform;
        t.SetParent(transform, false);
        t.localPosition = new Vector3(0f, hoverHeight, 0f);
        t.localRotation = src.transform.localRotation;
        t.localScale = src.transform.localScale * Mathf.Max(0.01f, displayScale);
        SetLayerRecursively(currentModel, gameObject.layer); // render like the box (world camera)
        currentModel.SetActive(true);
    }

    // Read the pending weapon's model straight from the public pool. When weaponPool is empty the
    // box falls back to a built-in pool whose models are null anyway, so returning null is correct.
    private GameObject ResolvePrizeModel(int idx)
    {
        var pool = box.weaponPool;
        if (pool == null || idx < 0 || idx >= pool.Count)
        {
            return null;
        }
        Weapon w = pool[idx];
        return w != null ? w.weaponModel : null;
    }

    // Strip anything that could touch gameplay: no colliders, physics, or audio on the clone, and
    // no other behaviors ticking. Done immediately after Instantiate (before any FixedUpdate).
    private static void MakeVisualOnly(GameObject go)
    {
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
        {
            Destroy(c);
        }
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))
        {
            Destroy(rb);
        }
        foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true))
        {
            Destroy(a);
        }
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb != null)
            {
                mb.enabled = false;
            }
        }
    }

    private void DestroyModel()
    {
        if (currentModel != null)
        {
            Destroy(currentModel);
        }
        currentModel = null;
        currentIndex = -1;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
