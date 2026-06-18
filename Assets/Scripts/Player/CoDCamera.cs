using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CoDCamera : MonoBehaviour
{
    [Header("Look Settings")]
    [Tooltip("Adjusts how fast the camera moves.")]
    public float mouseSensitivity = 200f;

    [Tooltip("Drag the parent Player Object here.")]
    public Transform playerBody;
    public bool invertY = false;
    public float minimumPitch = -85f;
    public float maximumPitch = 85f;

    [Header("Aim Down Sights (ADS)")]
    [Tooltip("Standard Field of View (CoD often uses 90+ for Zombies)")]
    public float defaultFOV = 90f;
    
    [Tooltip("Field of View when holding Right-Click")]
    public float adsFOV = 60f;
    
    [Tooltip("How fast the camera zooms in and out")]
    public float adsSpeed = 15f;

    [Header("Cursor")]
    public bool lockCursorOnStart = true;
    public KeyCode unlockCursorKey = KeyCode.Escape;

    private float xRotation = 0f;
    private Camera cam;
    private bool cursorLocked;

    public bool IsAiming { get; private set; }

    void Start()
    {
        cam = GetComponent<Camera>();
        cam.fieldOfView = defaultFOV;

        if (playerBody == null && transform.parent != null)
        {
            playerBody = transform.parent;
        }

        if (lockCursorOnStart)
        {
            SetCursorLocked(true);
        }
    }

    void Update()
    {
        HandleCursorLock();
        HandleMouseLook();
        HandleADS();
    }

    private void HandleMouseLook()
    {
        if (!cursorLocked || playerBody == null)
        {
            return;
        }

        // Get mouse input and multiply by sensitivity and time
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        // Calculate vertical rotation
        xRotation += invertY ? mouseY : -mouseY;

        // Clamp the vertical looking angle so the player can't snap their neck
        xRotation = Mathf.Clamp(xRotation, minimumPitch, maximumPitch);

        // Apply vertical rotation to the camera itself
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // Apply horizontal rotation to the whole player body
        playerBody.Rotate(Vector3.up * mouseX);
    }

    private void HandleADS()
    {
        // "Fire2" is Unity's default input for Right Mouse Button
        IsAiming = Input.GetButton("Fire2") && cursorLocked;
        float targetFOV = IsAiming ? adsFOV : defaultFOV;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, adsSpeed * Time.deltaTime);
    }

    private void HandleCursorLock()
    {
        if (Input.GetKeyDown(unlockCursorKey))
        {
            SetCursorLocked(false);
        }

        if (!cursorLocked && Input.GetMouseButtonDown(0))
        {
            SetCursorLocked(true);
        }
    }

    public void SetSensitivity(float sensitivity)
    {
        mouseSensitivity = Mathf.Max(1f, sensitivity);
    }

    private void SetCursorLocked(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
