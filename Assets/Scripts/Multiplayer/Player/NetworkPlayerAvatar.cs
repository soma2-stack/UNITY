using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkPlayerAvatar : NetworkBehaviour
{
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

    private readonly NetworkVariable<FixedString64Bytes> displayName = new NetworkVariable<FixedString64Bytes>();
    private readonly NetworkVariable<float> movementSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> crouching = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private TMP_Text worldName;

    public string DisplayName => displayName.Value.ToString();

    public override void OnNetworkSpawn()
    {
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
        if (IsOwner && movement != null && movement.enabled)
        {
            movementSpeed.Value = movement.MoveInput.magnitude * movement.CurrentSpeed;
            crouching.Value = movement.IsCrouching;
        }

        if (animator != null)
        {
            animator.SetFloat("Speed", movementSpeed.Value, 0.1f, Time.deltaTime);
            animator.SetBool("Crouching", crouching.Value);
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

        foreach (Renderer survivorRenderer in survivorRenderers)
        {
            if (survivorRenderer != null)
            {
                survivorRenderer.enabled = !IsOwner;
            }
        }

        if (!IsOwner)
        {
            BuildWorldName();
        }
    }

    private void ResolveReferences()
    {
        movement ??= GetComponent<PlayerMovement>();
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
        foreach (Renderer survivorRenderer in survivorRenderers)
        {
            if (survivorRenderer == null)
            {
                continue;
            }

            foreach (Material material in survivorRenderer.materials)
            {
                material.color = color;
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

        foreach (Renderer survivorRenderer in survivorRenderers)
        {
            if (survivorRenderer != null)
            {
                survivorRenderer.enabled = gameplay && !IsOwner;
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
