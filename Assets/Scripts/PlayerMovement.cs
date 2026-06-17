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
    public float gravity = -19.62f; // Snappy, heavy gravity
    public float jumpHeight = 1.2f;
    public float airControl = 0.35f;

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

    public bool IsGrounded => isGrounded;
    public bool IsCrouching => isCrouching;
    public Vector2 MoveInput { get; private set; }
    public float CurrentSpeed { get; private set; }

    void Start()
    {
        // Lock the mouse cursor to the center of the screen and hide it
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        controller = GetComponent<CharacterController>();
        standingHeight = controller.height;
        originalControllerCenter = controller.center;

        if (playerCamera != null)
        {
            codCamera = playerCamera.GetComponent<CoDCamera>();
            standingCameraLocalPosition = playerCamera.localPosition;
            crouchingCameraLocalPosition = standingCameraLocalPosition;
            crouchingCameraLocalPosition.y -= (standingHeight - crouchHeight) * 0.5f;
        }
    }

    void Update()
    {
        if (codCamera == null || !codCamera.enabled)
        {
            HandleMouseLook();
        }

        HandleCrouch();
        HandleMovement();
        HandleFootsteps();
    }

    void HandleMouseLook()
    {
        if (playerCamera == null)
        {
            return;
        }

        // Get raw mouse input
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Rotate the camera up and down (Pitch)
        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -90f, 90f); // Stops you from breaking your neck
        playerCamera.localEulerAngles = Vector3.right * cameraPitch;

        // Rotate the player body left and right (Yaw)
        transform.Rotate(Vector3.up * mouseX);
    }

    void HandleMovement()
    {
        // Check if touching the floor
        isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; // Force player flush against the ground
        }

        // Get WASD input
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        MoveInput = Vector2.ClampMagnitude(new Vector2(x, z), 1f);

        // Calculate direction relative to where the player is looking
        Vector3 move = transform.right * MoveInput.x + transform.forward * MoveInput.y;

        bool wantsToSprint = Input.GetKey(KeyCode.LeftShift) && MoveInput.y > 0.1f && !isCrouching;
        CurrentSpeed = isCrouching ? crouchSpeed : wantsToSprint ? sprintSpeed : walkSpeed;

        if (!isGrounded)
        {
            move *= airControl;
        }

        // Move the player
        controller.Move(move * CurrentSpeed * Time.deltaTime);

        // Handle Jumping
        if (Input.GetButtonDown("Jump") && isGrounded && !isCrouching)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // Apply gravity
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    private void HandleCrouch()
    {
        bool wantsToCrouch = Input.GetKey(crouchKey) || Input.GetKey(KeyCode.C);
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
        if (footstepSource == null || footstepClips == null || footstepClips.Length == 0)
        {
            return;
        }

        bool isMoving = MoveInput.sqrMagnitude > 0.01f;
        if (!isGrounded || !isMoving)
        {
            stepTimer = 0f;
            return;
        }

        float interval = isCrouching ? crouchStepInterval : CurrentSpeed >= sprintSpeed - 0.1f ? sprintStepInterval : walkStepInterval;
        stepTimer += Time.deltaTime;

        if (stepTimer < interval)
        {
            return;
        }

        stepTimer = 0f;
        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
        footstepSource.PlayOneShot(clip, footstepVolume);
    }
}
