using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkPlayerAvatar : NetworkBehaviour
{
    // Animator parameters - MUST match Assets/Animations/PlayerLocomotion.controller
    // (built by the "Set Up Player" tool): Speed (float), Sprint (bool), Crouch (bool).
    private const string SpeedParam = "Speed";
    private const string SprintParam = "Sprint";
    private const string CrouchParam = "Crouch";

    private static readonly Color[] SurvivorColors =
    {
        new Color(0.34f, 0.48f, 0.36f),
        new Color(0.42f, 0.38f, 0.3f),
        new Color(0.28f, 0.38f, 0.48f),
        new Color(0.48f, 0.3f, 0.28f)
    };

    private static readonly Vector3[] SpawnOffsets =
    {
        new Vector3(-2.2f, 0f, -1.5f),
        new Vector3(2.2f, 0f, -1.5f),
        new Vector3(-2.2f, 0f, 1.5f),
        new Vector3(2.2f, 0f, 1.5f)
    };

    [SerializeField] private PlayerMovement movement;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;
    [SerializeField] private Animator animator;
    [SerializeField] private Renderer[] survivorRenderers;

    [Tooltip("The selectable western body models (e.g. sheriff / gunman / outlow). " +
             "One is enabled per OwnerClientId; the rest are disabled.")]
    [SerializeField] private GameObject[] modelVariants;

    private readonly NetworkVariable<FixedString64Bytes> displayName = new NetworkVariable<FixedString64Bytes>();
    private readonly NetworkVariable<float> movementSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> sprinting = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> crouching = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private PlayerAnimator playerAnimator;
    private TMP_Text worldName;

    public string DisplayName => displayName.Value.ToString();

    public override void OnNetworkSpawn()
    {
        SelectModelVariant();
        ResolveReferences();
        SceneManager.sceneLoaded += OnSceneLoaded;
        displayName.OnValueChanged += OnDisplayNameChanged;
        ConfigureOwnership();
        ApplySurvivorColor();

        if (IsOwner)
        {
            string preferredName = PlayerPrefs.GetString("MultiplayerDisplayName", "Survivor");
            SetDisplayNameServerRpc(MultiplayerSessionController.SanitizeDisplayName(preferredName));
        }

        RefreshForScene(SceneManager.GetActiveScene());
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        displayName.OnValueChanged -= OnDisplayNameChanged;
    }

    private void Update()
    {
        // The owner replicates its real locomotion state for everyone else to read.
        if (IsOwner && movement != null && movement.enabled)
        {
            movementSpeed.Value = movement.MoveInput.magnitude * movement.CurrentSpeed;
            sprinting.Value = movement.CurrentSpeed > (movement.sprintSpeed - 0.5f);
            crouching.Value = movement.IsCrouching;
        }

        // Remote bodies are animated from the replicated NetworkVariables. The owner's
        // own body is driven by PlayerAnimator (which we leave enabled only for the owner),
        // so the avatar does NOT touch the animator for the owner to avoid double-driving.
        if (!IsOwner && animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetFloat(SpeedParam, movementSpeed.Value, 0.1f, Time.deltaTime);
            animator.SetBool(SprintParam, sprinting.Value);
            animator.SetBool(CrouchParam, crouching.Value);
        }

        if (worldName != null && Camera.main != null)
        {
            worldName.transform.rotation = Quaternion.LookRotation(worldName.transform.position - Camera.main.transform.position);
        }
    }

    [ServerRpc]
    private void SetDisplayNameServerRpc(FixedString64Bytes value)
    {
        displayName.Value = value;
    }

    // Per-player model variety: pick one body based on OwnerClientId and disable the
    // others, so the four survivors look different (cycles sheriff / gunman / outlow).
    private void SelectModelVariant()
    {
        if (modelVariants == null || modelVariants.Length == 0)
        {
            return;
        }

        int chosen = (int)(OwnerClientId % (ulong)modelVariants.Length);
        for (int i = 0; i < modelVariants.Length; i++)
        {
            if (modelVariants[i] != null)
            {
                modelVariants[i].SetActive(i == chosen);
            }
        }

        // Point the animator + renderer references at the visible body and rebind so
        // the chosen body actually plays the locomotion controller.
        GameObject body = modelVariants[chosen];
        if (body != null)
        {
            Animator bodyAnimator = body.GetComponentInChildren<Animator>(true);
            if (bodyAnimator != null)
            {
                animator = bodyAnimator;
                animator.Rebind();
            }
            survivorRenderers = body.GetComponentsInChildren<Renderer>(true);
        }
    }

    private void ConfigureOwnership()
    {
        if (movement != null)
        {
            movement.enabled = IsOwner;
        }

        if (playerCamera != null)
        {
            playerCamera.enabled = IsOwner;
            CoDCamera cameraController = playerCamera.GetComponent<CoDCamera>();
            if (cameraController != null)
            {
                cameraController.enabled = IsOwner;
            }
        }

        if (audioListener != null)
        {
            audioListener.enabled = IsOwner;
        }

        // The owner uses PlayerAnimator to drive their own body; remote players are
        // driven by this avatar from the replicated NetworkVariables. Disable
        // PlayerAnimator on non-owners so the two systems never fight.
        if (playerAnimator != null)
        {
            playerAnimator.enabled = IsOwner;
        }

        // Weapons (firing / switching / first-person model) belong to the owner only.
        WeaponController weaponController = GetComponent<WeaponController>();
        if (weaponController != null)
        {
            weaponController.enabled = IsOwner;
        }

        ApplyBodyVisibility();

        if (!IsOwner)
        {
            BuildWorldName();
        }
    }

    // First-person view: the owner's own body is hidden from their camera but still
    // casts a shadow (ShadowsOnly), mirroring FirstPersonView. Remote players see the
    // full body normally.
    private void ApplyBodyVisibility()
    {
        if (survivorRenderers == null)
        {
            return;
        }

        ShadowCastingMode mode = IsOwner ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        foreach (Renderer survivorRenderer in survivorRenderers)
        {
            if (survivorRenderer == null)
            {
                continue;
            }

            survivorRenderer.enabled = true;
            survivorRenderer.shadowCastingMode = mode;
        }
    }

    private void ResolveReferences()
    {
        movement ??= GetComponent<PlayerMovement>();
        playerAnimator ??= GetComponent<PlayerAnimator>();
        playerCamera ??= GetComponentInChildren<Camera>(true);
        audioListener ??= GetComponentInChildren<AudioListener>(true);
        animator ??= GetComponentInChildren<Animator>(true);
        if (survivorRenderers == null || survivorRenderers.Length == 0)
        {
            survivorRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }

    private void ApplySurvivorColor()
    {
        Color color = SurvivorColors[(int)(OwnerClientId % (ulong)SurvivorColors.Length)];
        if (survivorRenderers == null)
        {
            return;
        }

        foreach (Renderer survivorRenderer in survivorRenderers)
        {
            if (survivorRenderer == null)
            {
                continue;
            }

            foreach (Material material in survivorRenderer.materials)
            {
                if (material.HasProperty("_Color"))
                {
                    material.color = color;
                }
                else if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", color);
                }
            }
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshForScene(scene);
    }

    private void RefreshForScene(Scene scene)
    {
        bool gameplay = scene.name == "SchoolOfTheDead";
        if (movement != null)
        {
            movement.enabled = gameplay && IsOwner;
        }

        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(gameplay && IsOwner);
        }

        if (survivorRenderers != null)
        {
            foreach (Renderer survivorRenderer in survivorRenderers)
            {
                if (survivorRenderer != null)
                {
                    // Owner keeps a shadow-only body in gameplay; remote shows full body.
                    survivorRenderer.enabled = gameplay;
                }
            }
        }

        if (worldName != null)
        {
            worldName.gameObject.SetActive(gameplay && !IsOwner);
        }

        if (gameplay && IsOwner)
        {
            Vector3 baseSpawn = new Vector3(0f, 1.5f, -7.67f);
            transform.position = baseSpawn + SpawnOffsets[(int)(OwnerClientId % (ulong)SpawnOffsets.Length)];
            transform.rotation = Quaternion.identity;
        }
    }

    private void BuildWorldName()
    {
        GameObject canvasObject = new GameObject("Player Name", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        canvasObject.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(2.4f, 0.4f);
        canvasObject.transform.localScale = Vector3.one * 0.01f;

        GameObject textObject = new GameObject("Name", typeof(RectTransform));
        textObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        worldName = textObject.AddComponent<TextMeshProUGUI>();
        worldName.text = DisplayName;
        worldName.fontSize = 28f;
        worldName.alignment = TextAlignmentOptions.Center;
        worldName.color = new Color(0.94f, 0.92f, 0.86f, 0.9f);
    }

    private void OnDisplayNameChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        if (worldName != null)
        {
            worldName.text = current.ToString();
        }
    }
}
