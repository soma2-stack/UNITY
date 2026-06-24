using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class CoDMovement : MonoBehaviour
{
    private CharacterController controller;

    [Header("Movement Speeds")]
    [Tooltip("Standard walking speed")]
    public float walkSpeed = 5f;
    [Tooltip("Speed when holding Left Shift")]
    public float sprintSpeed = 9f;

    [Header("Jumping & Gravity")]
    [Tooltip("How high the player jumps")]
    public float jumpHeight = 1.5f;
    [Tooltip("Standard gravity is -9.81, CoD feels slightly heavier")]
    public float gravity = -19.62f; 

    [Header("Ground Detection")]
    [Tooltip("An empty GameObject placed at the bottom of the player")]
    public Transform groundCheck;
    [Tooltip("Radius of the ground check sphere")]
    public float groundDistance = 0.4f;
    [Tooltip("Which layers count as 'Ground'")]
    public LayerMask groundMask;

    private Vector3 velocity;
    private bool isGrounded;

    void Start()
    {
        // Automatically grab the CharacterController on the Player
        controller = GetComponent<CharacterController>();
    }

    void Awake()
    {
        // PlayerMovement is the real controller used by the game. If both end up on
        // the same player they would fight over CharacterController.Move every frame,
        // so this legacy mover defers and disables itself when PlayerMovement is present.
        if (GetComponent<PlayerMovement>() != null)
        {
            enabled = false;
        }
    }

    void Update()
    {
        HandleMovement();
        HandleJumpAndGravity();
    }

    private void HandleMovement()
    {
        // Get WASD / Controller Left Stick input
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // Move relative to the direction the player body is facing
        Vector3 move = transform.right * x + transform.forward * z;

        // Determine if sprinting (Left Shift is the standard PC sprint key)
        float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;

        // Apply movement
        controller.Move(move * currentSpeed * Time.deltaTime);
    }

    private void HandleJumpAndGravity()
    {
        // Creates a tiny invisible sphere at the feet to check for the Ground layer.
        // Falls back to the controller's own grounded check if no groundCheck is wired
        // (prevents a NullReferenceException on an unconfigured prefab).
        isGrounded = groundCheck != null
            ? Physics.CheckSphere(groundCheck.position, groundDistance, groundMask)
            : controller.isGrounded;

        // Reset gravity accumulation if we are standing on the floor
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; // A small negative number keeps the player snapped to slopes
        }

        // Jump mechanics
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            // Physics formula for calculating the velocity needed to reach a specific height
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // Apply gravity over time (freefall acceleration)
        velocity.y += gravity * Time.deltaTime;
        
        // Move the controller downward based on gravity/jump velocity
        controller.Move(velocity * Time.deltaTime);
    }
}