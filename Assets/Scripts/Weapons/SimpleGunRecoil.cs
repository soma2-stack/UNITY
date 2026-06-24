using UnityEngine;

public class SimpleGunRecoil : MonoBehaviour
{
    [Header("Kickback")]
    public Vector3 kickBackPosition = new Vector3(0f, 0f, -0.08f);
    public Vector3 kickBackRotation = new Vector3(-6f, 1f, 0f);

    [Header("Speed")]
    public float kickSpeed = 25f;
    public float returnSpeed = 12f;

    [Header("Shot Effects")]
    public ParticleSystem muzzleFlash;
    public AudioSource gunAudio;
    public AudioClip gunshotSound;

    private Vector3 startPosition;
    private Quaternion startRotation;

    private Vector3 targetPosition;
    private Quaternion targetRotation;

    private void Start()
    {
        startPosition = transform.localPosition;
        startRotation = transform.localRotation;

        targetPosition = startPosition;
        targetRotation = startRotation;
    }

    private void Update()
    {
        transform.localPosition = Vector3.Lerp(
            transform.localPosition,
            targetPosition,
            Time.deltaTime * kickSpeed
        );

        transform.localRotation = Quaternion.Slerp(
            transform.localRotation,
            targetRotation,
            Time.deltaTime * kickSpeed
        );

        targetPosition = Vector3.Lerp(
            targetPosition,
            startPosition,
            Time.deltaTime * returnSpeed
        );

        targetRotation = Quaternion.Slerp(
            targetRotation,
            startRotation,
            Time.deltaTime * returnSpeed
        );
    }

    public void Kick()
    {
        targetPosition = startPosition + kickBackPosition;
        targetRotation = startRotation * Quaternion.Euler(kickBackRotation);

        if (muzzleFlash != null)
        {
            muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            muzzleFlash.Play();
        }

        if (gunAudio != null && gunshotSound != null)
        {
            gunAudio.PlayOneShot(gunshotSound);
        }
    }
}