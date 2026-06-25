using UnityEngine;

/// <summary>
/// Drives a Humanoid player Animator from the existing <see cref="PlayerMovement"/>
/// so the visible character body plays idle / walk-run / sprint / crouch.
///
/// Put this on the player root (next to PlayerMovement). The Animator can be on
/// the same object or on the character model child. All null-safe: if there is no
/// Animator or controller yet, it simply does nothing.
///
/// Parameters expected on the controller (built by the "Set Up Player" tool):
///   Speed  (float)  - current planar move speed
///   Sprint (bool)   - true while sprinting
///   Crouch (bool)   - true while crouched
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerAnimator : MonoBehaviour
{
    [Tooltip("Animator float parameter for move speed (idle <-> move <-> sprint).")]
    public string speedParam = "Speed";
    [Tooltip("Animator bool parameter set while sprinting.")]
    public string sprintParam = "Sprint";
    [Tooltip("Animator bool parameter set while crouched.")]
    public string crouchParam = "Crouch";
    [Tooltip("How quickly the Speed value eases toward the target while moving (higher = snappier).")]
    public float speedDamp = 14f;
    [Tooltip("How quickly the Speed value drops to 0 when you stop (higher = stops sooner, no run-on).")]
    public float stopDamp = 22f;

    private PlayerMovement movement;
    private Animator animator;
    private bool hasSpeed;
    private bool hasSprint;
    private bool hasCrouch;
    private float smoothedSpeed;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        animator = GetComponentInChildren<Animator>();
        CacheParams();
    }

    private void CacheParams()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.name == speedParam) hasSpeed = true;
            else if (p.name == sprintParam) hasSprint = true;
            else if (p.name == crouchParam) hasCrouch = true;
        }
    }

    private void Update()
    {
        if (movement == null || animator == null)
        {
            return;
        }

        // Target speed is the actual move speed when there is input, else 0 (idle).
        float target = movement.MoveInput.sqrMagnitude > 0.01f ? movement.CurrentSpeed : 0f;

        // Ease toward the target; drop to idle faster when stopping so the run
        // animation doesn't keep playing after the player releases the keys.
        float damp = target <= 0.01f ? stopDamp : speedDamp;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, target, Mathf.Clamp01(damp * Time.deltaTime));
        if (target <= 0.01f && smoothedSpeed < 0.15f)
        {
            smoothedSpeed = 0f;
        }

        if (hasSpeed)
        {
            animator.SetFloat(speedParam, smoothedSpeed);
        }
        if (hasSprint)
        {
            // Sprinting when moving forward near the sprint speed.
            bool sprinting = target > (movement.sprintSpeed - 0.5f);
            animator.SetBool(sprintParam, sprinting);
        }
        if (hasCrouch)
        {
            animator.SetBool(crouchParam, movement.IsCrouching);
        }
    }
}
