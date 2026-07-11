using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkPlayerAvatar : NetworkBehaviour
{
    // Animator parameters - MUST match the real player controller
    // (Assets/Animations/PlayerAnimator.controller), which PlayerMovement drives:
    // MoveX / MoveZ (float), IsMoving / IsSprinting (bool). Jump is a one-shot local
    // trigger and is not replicated to remote bodies.
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int IsSprintingHash = Animator.StringToHash("IsSprinting");

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
    private readonly NetworkVariable<float> moveX = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> moveZ = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> isMoving = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> isSprinting = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private PlayerAnimator playerAnimator;
    private TMP_Text worldName;

    // --- Spectator (Checkpoint 2) ---
    // When a player FINALLY dies (bled out, not merely downed) they soft-despawn: the body
    // is hidden and its collider disabled on every peer, and the LOCAL owner switches to a
    // spectator camera following a living teammate.
    private PlayerHealth health;
    private CharacterController characterController;
    private bool deathHandled;
    private bool spectating;
    private PlayerHealth spectateTarget;
    private float nextTeammateScanTime;

    public string DisplayName => displayName.Value.ToString();

    private void Awake()
    {
        ResolveReferences();
        SetOwnerOnlyComponents(false);
    }

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
            // Make THIS peer's player the one HUD / interactables / grants resolve.
            LocalPlayer.Register(gameObject);

            string preferredName = PlayerPrefs.GetString("MultiplayerDisplayName", "Survivor");
            SetDisplayNameServerRpc(MultiplayerSessionController.SanitizeDisplayName(preferredName));
        }

        // Soft-despawn / spectate hook: every copy reacts to this player's FINAL death.
        health = GetComponent<PlayerHealth>();
        characterController = GetComponent<CharacterController>();
        if (health != null)
        {
            health.OnPlayerDied += HandleDeath;
            // Late-join safety: if we spawned into an already-dead player, reflect it now.
            if (health.IsDead)
            {
                HandleDeath();
            }
        }

        RefreshForScene(SceneManager.GetActiveScene());
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        displayName.OnValueChanged -= OnDisplayNameChanged;
        if (health != null)
        {
            health.OnPlayerDied -= HandleDeath;
        }
        if (IsOwner)
        {
            LocalPlayer.Unregister(gameObject);
        }
    }

    private void Update()
    {
        // The owner replicates its real locomotion state for everyone else to read.
        if (IsOwner && movement != null && movement.enabled)
        {
            moveX.Value = movement.MoveInput.x;
            moveZ.Value = movement.MoveInput.y;
            isMoving.Value = movement.MoveInput.sqrMagnitude > 0.01f;
            isSprinting.Value = movement.CurrentSpeed > (movement.sprintSpeed - 0.5f);
        }

        // Remote bodies are animated from the replicated NetworkVariables. The owner's
        // own body is driven by PlayerAnimator (which we leave enabled only for the owner),
        // so the avatar does NOT touch the animator for the owner to avoid double-driving.
        if (!IsOwner && animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetFloat(MoveXHash, moveX.Value, 0.1f, Time.deltaTime);
            animator.SetFloat(MoveZHash, moveZ.Value, 0.1f, Time.deltaTime);
            animator.SetBool(IsMovingHash, isMoving.Value);
            animator.SetBool(IsSprintingHash, isSprinting.Value);
        }

        if (worldName != null && Camera.main != null)
        {
            worldName.transform.rotation = Quaternion.LookRotation(worldName.transform.position - Camera.main.transform.position);
        }
    }

    // Drive the spectator camera AFTER everything else has moved this frame, so the view
    // tracks the living teammate's final position instead of lagging a frame behind.
    private void LateUpdate()
    {
        if (spectating)
        {
            UpdateSpectate();
        }
    }

    [ServerRpc]
    private void SetDisplayNameServerRpc(FixedString64Bytes value)
    {
        displayName.Value = value;
    }

    /// <summary>
    /// A player who has FINALLY died (not merely downed) soft-despawns. Runs on EVERY copy:
    /// the body's collider is disabled and its renderers/nameplate hidden so the corpse stops
    /// blocking zombies, teammates and shots. The LOCAL owner additionally enters spectator
    /// mode (see <see cref="EnterSpectate"/>). Downed players never reach here — this is wired
    /// to OnPlayerDied only, so a revivable teammate keeps their normal body and view.
    /// </summary>
    private void HandleDeath()
    {
        if (deathHandled)
        {
            return;
        }
        deathHandled = true;

        // Remove the dead body from the world on every peer.
        if (characterController != null)
        {
            characterController.enabled = false;
        }
        if (survivorRenderers != null)
        {
            foreach (Renderer survivorRenderer in survivorRenderers)
            {
                if (survivorRenderer != null)
                {
                    survivorRenderer.enabled = false;
                }
            }
        }
        if (worldName != null)
        {
            worldName.gameObject.SetActive(false);
        }

        if (IsOwner)
        {
            EnterSpectate();
        }
    }

    // Switch the LOCAL dead player into spectator mode: stop movement and first-person
    // camera control, but keep the Camera + AudioListener enabled so they can keep watching
    // and hearing the match. Weapon firing/melee is already blocked by PlayerHealth.IsDead
    // inside WeaponController, so no weapon handling is needed here.
    private void EnterSpectate()
    {
        spectating = true;

        if (movement != null)
        {
            movement.enabled = false;
        }
        if (playerCamera != null)
        {
            CoDCamera cameraController = playerCamera.GetComponent<CoDCamera>();
            if (cameraController != null)
            {
                cameraController.enabled = false;
            }
        }
    }

    // Follow a living teammate with an over-the-shoulder view. If none are left the method
    // does nothing and the (all-dead) game-over screen takes over.
    private void UpdateSpectate()
    {
        if (playerCamera == null)
        {
            return;
        }

        // Keep the current target while it lives; re-pick when it dies or leaves. The scan is
        // throttled so that if nobody is alive (the brief window before the game-over screen)
        // we don't run FindObjectsByType every frame.
        if (spectateTarget == null || spectateTarget.IsDead)
        {
            if (Time.time >= nextTeammateScanTime)
            {
                nextTeammateScanTime = Time.time + 0.5f;
                spectateTarget = FindLivingTeammate();
            }
        }
        if (spectateTarget == null || spectateTarget.IsDead)
        {
            return;
        }

        Transform targetRoot = spectateTarget.transform;
        Camera targetCam = spectateTarget.GetComponentInChildren<Camera>(true);
        Vector3 eye = targetCam != null
            ? targetCam.transform.position
            : targetRoot.position + Vector3.up * 1.6f;
        Vector3 forward = targetRoot.forward;

        Vector3 camPos = eye - forward * 3f + Vector3.up * 1.1f;
        Vector3 lookAt = eye + forward * 2f;
        playerCamera.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(lookAt - camPos));
    }

    // Nearest still-living player that isn't this (dead) one.
    private PlayerHealth FindLivingTeammate()
    {
        PlayerHealth best = null;
        float bestSqr = float.MaxValue;
        Vector3 here = transform.position;

        PlayerHealth[] all = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        foreach (PlayerHealth ph in all)
        {
            if (ph == null || ph == health || ph.IsDead)
            {
                continue;
            }
            float sqr = (ph.transform.position - here).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = ph;
            }
        }
        return best;
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
        SetOwnerOnlyComponents(IsOwner);

        // Owner sees their own body as shadows-only; every remote copy stays visible.
        ApplyBodyVisibility();

        if (!IsOwner)
        {
            BuildWorldName();
        }
    }

    private void SetOwnerOnlyComponents(bool enabledForOwner)
    {
        if (movement != null)
        {
            movement.enabled = enabledForOwner;
        }

        if (playerCamera != null)
        {
            playerCamera.enabled = enabledForOwner;
            CoDCamera cameraController = playerCamera.GetComponent<CoDCamera>();
            if (cameraController != null)
            {
                cameraController.enabled = enabledForOwner;
            }
        }

        if (audioListener != null)
        {
            audioListener.enabled = enabledForOwner;
        }

        if (playerAnimator != null)
        {
            playerAnimator.enabled = enabledForOwner;
        }

        // Weapons are owner-only for input and first-person view setup. Weapon behavior
        // itself is left unchanged for the later weapons checkpoint.
        WeaponController weaponController = GetComponent<WeaponController>();
        if (weaponController != null)
        {
            weaponController.enabled = enabledForOwner || IsServer;
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
        // Auto-fill renderers if unset, empty, or only null entries (stale prefab wiring).
        if (survivorRenderers == null || survivorRenderers.Length == 0 || AllNull(survivorRenderers))
        {
            survivorRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }

    private static bool AllNull(Renderer[] renderers)
    {
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                return false;
            }
        }
        return true;
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
            playerCamera.enabled = gameplay && IsOwner;
            CoDCamera cameraController = playerCamera.GetComponent<CoDCamera>();
            if (cameraController != null)
            {
                cameraController.enabled = gameplay && IsOwner;
            }
            playerCamera.gameObject.SetActive(gameplay && IsOwner);
        }

        if (audioListener != null)
        {
            audioListener.enabled = gameplay && IsOwner;
        }

        if (playerAnimator != null)
        {
            playerAnimator.enabled = gameplay && IsOwner;
        }

        WeaponController weaponController = GetComponent<WeaponController>();
        if (weaponController != null)
        {
            weaponController.enabled = gameplay && (IsOwner || IsServer);
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
            if (gameplay)
            {
                ApplyBodyVisibility();
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
