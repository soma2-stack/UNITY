using UnityEngine;

/// <summary>
/// Keeps the security-room breaker indicator synchronized with the authoritative
/// map power state. The indicator is red while power is off and green once power
/// has been activated, including network-applied state changes.
/// </summary>
[DisallowMultipleComponent]
public sealed class SecurityRoomPowerLight : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Light statusLight;
    [SerializeField] private Renderer indicatorRenderer;
    [SerializeField] private Material powerOffMaterial;
    [SerializeField] private Material powerOnMaterial;

    [Header("Light State")]
    [SerializeField] private Color powerOffColor = new Color(1f, 0.16f, 0.05f, 1f);
    [SerializeField] private Color powerOnColor = new Color(0.12f, 1f, 0.28f, 1f);
    [SerializeField, Min(0f)] private float powerOffIntensity = 1.15f;
    [SerializeField, Min(0f)] private float powerOnIntensity = 1.35f;

    private bool appliedState;
    private bool hasAppliedState;

    private void OnEnable()
    {
        hasAppliedState = false;
        ApplyStateIfChanged();
    }

    private void Update()
    {
        ApplyStateIfChanged();
    }

    private void ApplyStateIfChanged()
    {
        bool powerIsOn = PowerState.IsOn;
        if (hasAppliedState && powerIsOn == appliedState)
        {
            return;
        }

        appliedState = powerIsOn;
        hasAppliedState = true;

        if (statusLight != null)
        {
            statusLight.color = powerIsOn ? powerOnColor : powerOffColor;
            statusLight.intensity = powerIsOn ? powerOnIntensity : powerOffIntensity;
        }

        if (indicatorRenderer != null)
        {
            Material targetMaterial = powerIsOn ? powerOnMaterial : powerOffMaterial;
            if (targetMaterial != null)
            {
                indicatorRenderer.sharedMaterial = targetMaterial;
            }
        }
    }
}
