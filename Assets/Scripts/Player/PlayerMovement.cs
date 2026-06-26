using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Camera Settings")]
    public Transform playerCamera;
    public float mouseSensitivity = 2f;
    private float cameraPitch = 0f;

    [Header("Movement Settings")]
    public float walkSpeed = 4.5f;
    public float sprintSpeed = 7.5f;
    public float crouchSpeed = 2.5f;
    [Tooltip("Speed multiplier applied to all movement (Stamin-Up perk). 1 = normal.")]
    public float speedMultiplier = 1f;
    public float gravity = -19.62f;
    public float jumpHeight = 1.2f;
    public float airControl = 0.35f;
    [Tooltip("Speed multiplier applied while downed (crawl). 0.4 = 40% of walk speed.")]
    public float downedSpeedMultiplier = 0.4f;

    [Header("Crouch Settings")]
    public KeyCode crouchKey = KeyCode.LeftControl;
    public float crouchHeight = 1.2f;
    public float crouchTransitionSpeed = 12f;

    [Header("Footsteps")]
    public AudioSource footstepSource;
    public AudioClip[] footstepClips;
    public float walkStepInterval = 0.55f;
    public float sprintStepInterval = 0.35f;
    public float crouchStepInterval = 0.75f;
    [Range(0f, 1f)] public float footstepVolume = 0.45f;

    [Header("Animation")]
    public Animator animator;

    private CharacterController controller;
    private CoDCamera codCamera;
    private Vector3 velocity;
    private Vector3 standingCameraLocalPosition;
    private Vector3 crouchingCameraLocalPosition;
    private Vector3 originalControllerCenter;
    private float standingHeight;
    private float stepTimer;
    private bool isGrounded;
    private bool isCrouching;
    private bool isDowned;
    private PlayerHealth playerHealth;
    private NetworkPlayerAvatar networkAvatar; // cached for the multiplayer ownership check

    // Solo / not network-spawned: this peer always controls the player. In a session:
    // only the OWNING client drives movement (others are server/owner-replicated).
    private bool IsLocalOwner
    {
        get
        {
            if (networkAvatar == null)
            {
                networkAvatar = GetComponent<NetworkPlayerAvatar>();
            }
            if (networkAvatar == null || !networkAvatar.IsSpawned)
            {
                return true;
            }
            return networkAvatar.IsOwner;
        }
    }

    public bool IsGrounded => isGrounded;
    public bool IsCrouching => isCrouching;
    public Vector2 MoveInput { get; private set; }
    public float CurrentSpeed { get; private set; }

    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int IsSprintingHash = Animator.StringToHash("IsSprinting");
    private static readonly int JumpHash = Animator.StringToHash("Jump");

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        controller = GetComponent<CharacterController>();
        standingHeight = controller.height;
        originalControllerCenter = controller.center;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (playerCamera != null)
        {
            codCamera = playerCamera.GetComponent<CoDCamera>();
            standingCameraLocalPosition = playerCamera.localPosition;
            crouchingCameraLocalPosition = standingCameraLocalPosition;
            crouchingCameraLocalPosition.y -= (standingHeight - crouchHeight) * 0.5f;
        }

        playerHealth = GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.OnPlayerDowned += HandleDowned;
            playerHealth.OnPlayerRevived += HandleRevived;
        }
    }

    private void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.OnPlayerDowned -= HandleDowned;
            playerHealth.OnPlayerRevived -= HandleRevived;
        }
    }

    private void HandleDowned()
    {
        isDowned = true;
        if (codCamera != null) codCamera.SetDownedView(true);
    }

    private void HandleRevived()
    {
        isDowned = false;
        if (codCamera != null) codCamera.SetDownedView(false);
    }

    void Update()
    {
        // Multiplayer: only the owning client controls this player. Solo is unaffected.
        if (!IsLocalOwner)
        {
            return;
        }

        if (codCamera == null || !codCamera.enabled)
            HandleMouseLook();

        HandleCrouch();
        HandleMovement();
        HandleFootsteps();
    }

    void HandleMouseLook()
    {
        if (playerCamera == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        cameraPitch -= mouseY;
        float pitchLimit = isDowned ? 20f : 90f;
        cameraPitch = Mathf.Clamp(cameraPitch, -pitchLimit, pitchLimit);
        playerCamera.localEulerAngles = Vector3.right * cameraPitch;

        transform.Rotate(Vector3.up * mouseX);
    }

    void HandleMovement()
    {
        isGrounded = controller.isGrounded;

        if (isGrounded && velocity.y < 0)
            velocity.y = -2f;

        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        MoveInput = Vector2.ClampMagnitude(new Vector2(x, z), 1f);

        Vector3 move = transform.right * MoveInput.x + transform.forward * MoveInput.y;

        bool isMoving = MoveInput.sqrMagnitude > 0.01f;
        bool wantsToSprint = Input.GetKey(KeyCode.LeftShift) && MoveInput.y > 0.1f && !isCrouching && !isDowned;

        CurrentSpeed = isCrouching ? crouchSpeed : wantsToSprint ? sprintSpeed : walkSpeed;
        CurrentSpeed *= Mathf.Max(0f, speedMultiplier);

        if (isDowned)
            CurrentSpeed = walkSpeed * Mathf.Max(0f, downedSpeedMultiplier);

        if (!isGrounded)
            move *= airControl;

        controller.Move(move * CurrentSpeed * Time.deltaTime);

        bool jumpedThisFrame = false;

        if (Input.GetButtonDown("Jump") && isGrounded && !isCrouching && !isDowned)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpedThisFrame = true;
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        UpdateAnimator(isMoving, wantsToSprint, jumpedThisFrame);
    }

    private void UpdateAnimator(bool isMoving, bool isSprinting, bool jumpedThisFrame)
    {
        if (animator == null)
            return;

        animator.SetFloat(MoveXHash, MoveInput.x);
        animator.SetFloat(MoveZHash, MoveInput.y);
        animator.SetBool(IsMovingHash, isMoving);
        animator.SetBool(IsSprintingHash, isSprinting);

        if (jumpedThisFrame)
            animator.SetTrigger(JumpHash);
    }

    private void HandleCrouch()
    {
        bool wantsToCrouch = Input.GetKey(crouchKey) || Input.GetKey(KeyCode.C);

        // Releasing crouch: don't stand up if there's something directly above.
        if (!wantsToCrouch && isCrouching)
        {
            bool ceilingBlocked = Physics.SphereCast(
                transform.position + controller.center,
                controller.radius * 0.9f,
                Vector3.up,
                out _,
                (standingHeight - crouchHeight) * 0.5f + 0.05f,
                ~0,
                QueryTriggerInteraction.Ignore);

            if (ceilingBlocked) wantsToCrouch = true;
        }

        isCrouching = wantsToCrouch;

        float targetHeight = isCrouching ? crouchHeight : standingHeight;
        Vector3 targetCenter = originalControllerCenter;
        targetCenter.y -= (standingHeight - targetHeight) * 0.5f;

        controller.height = Mathf.Lerp(controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
        controller.center = Vector3.Lerp(controller.center, targetCenter, crouchTransitionSpeed * Time.deltaTime);

        if (playerCamera != null)
        {
            Vector3 targetCameraPosition = isCrouching ? crouchingCameraLocalPosition : standingCameraLocalPosition;
            playerCamera.localPosition = Vector3.Lerp(playerCamera.localPosition, targetCameraPosition, crouchTransitionSpeed * Time.deltaTime);
        }
    }

    private void HandleFootsteps()
    {
        if (footstepSource == null || footstepClips == null || footstepClips.Length == 0) return;

        bool isMoving = MoveInput.sqrMagnitude > 0.01f;

        if (!isGrounded || !isMoving)
        {
            stepTimer = 0f;
            return;
        }

        float sprintThreshold = sprintSpeed * Mathf.Max(0f, speedMultiplier) - 0.1f;
        float interval = isCrouching ? crouchStepInterval : CurrentSpeed >= sprintThreshold ? sprintStepInterval : walkStepInterval;

        stepTimer += Time.deltaTime;

        if (stepTimer < interval) return;

        stepTimer = 0f;

        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
        footstepSource.PlayOneShot(clip, footstepVolume);
    }
}