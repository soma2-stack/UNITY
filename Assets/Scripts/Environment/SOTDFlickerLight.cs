using UnityEngine;

/// <summary>
/// Cosmetic flicker for failing school lights (detail pass). Local-only, no gameplay effect:
/// randomly dips the light's intensity and optionally swaps the fixture's emissive material.
/// </summary>
[RequireComponent(typeof(Light))]
public class SOTDFlickerLight : MonoBehaviour
{
    [Tooltip("Average seconds between flicker bursts.")]
    public float burstInterval = 4f;
    [Tooltip("0 = subtle dips, 1 = frequent blackouts.")]
    [Range(0f, 1f)] public float severity = 0.5f;
    public Renderer fixtureRenderer;
    public Material onMaterial;
    public Material offMaterial;

    Light lightSource;
    float baseIntensity;
    float nextBurst;
    float burstEnd;
    float nextToggle;
    bool dark;
    bool shownDark;

    void Awake()
    {
        lightSource = GetComponent<Light>();
        baseIntensity = lightSource.intensity;
        nextBurst = Time.time + Random.Range(0.5f, burstInterval * 2f);
    }

    void Update()
    {
        float t = Time.time;
        if (t >= nextBurst && t > burstEnd)
        {
            burstEnd = t + Random.Range(0.15f, 0.6f + severity);
            nextBurst = burstEnd + Random.Range(burstInterval * 0.5f, burstInterval * 1.5f);
        }

        bool inBurst = t < burstEnd;
        if (inBurst && t >= nextToggle)
        {
            dark = Random.value < 0.35f + severity * 0.4f;
            nextToggle = t + Random.Range(0.03f, 0.12f);
        }
        else if (!inBurst)
        {
            dark = false;
        }

        lightSource.intensity = dark ? baseIntensity * Random.Range(0f, 0.15f) : baseIntensity;
        if (fixtureRenderer != null && onMaterial != null && offMaterial != null && dark != shownDark)
        {
            shownDark = dark;
            Material[] mats = fixtureRenderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == onMaterial || mats[i] == offMaterial) mats[i] = dark ? offMaterial : onMaterial;
            fixtureRenderer.sharedMaterials = mats;
        }
    }
}
