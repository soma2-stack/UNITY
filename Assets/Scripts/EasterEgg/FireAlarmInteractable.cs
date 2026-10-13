using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A single fire alarm for the "False Alarm / Fire Drill" easter egg. Attach this to each
/// placed <c>fire1</c> prop. Press E within range to pull it; activation is routed through
/// <see cref="FireAlarmEasterEgg"/> → <see cref="NetworkGameplayCoordinator"/> so progress is
/// TEAM-WIDE and server-authoritative: the server counts each alarm key exactly once, tells
/// every peer to mark it activated, and awards the reward when all alarms are pulled.
///
/// Reuses the project's <see cref="InteractableBase"/> convention (proximity press-E prompt),
/// so no new interaction system is introduced. The alarm prop has no collider — interaction is
/// proximity based (horizontal distance + vertical tolerance), so that is fine.
/// </summary>
public sealed class FireAlarmInteractable : InteractableBase
{
    // Stable, cross-peer key so the server/clients agree on WHICH alarm was pulled. Every peer
    // loads the identical scene, so the authored position resolves to the same key everywhere.
    // The position suffix keeps 5 identically named "fire1" props distinct (the shared hierarchy
    // path key alone would collide when they share a name/parent).
    private static readonly Dictionary<string, FireAlarmInteractable> Registry =
        new Dictionary<string, FireAlarmInteractable>();

    private string alarmKey;
    private Vector3 basePosition;

    private bool activated;   // authoritative team-wide state has marked this alarm pulled
    private bool pending;     // a pull request is in flight; suppress the prompt / re-fire

    public string AlarmKey
    {
        get
        {
            if (string.IsNullOrEmpty(alarmKey))
            {
                alarmKey = BuildAlarmKey();
            }
            return alarmKey;
        }
    }

    private void Awake()
    {
        // Capture the authored world position now so the key is stable across peers.
        basePosition = transform.position;
        alarmKey = BuildAlarmKey();
        Registry[alarmKey] = this;
    }

    private void OnDestroy()
    {
        if (!string.IsNullOrEmpty(alarmKey) &&
            Registry.TryGetValue(alarmKey, out FireAlarmInteractable registered) &&
            registered == this)
        {
            Registry.Remove(alarmKey);
        }
    }

    private string BuildAlarmKey()
    {
        return "FIREALARM:" + BuildNetworkKey(transform) + "@" +
               Mathf.RoundToInt(basePosition.x * 10f) + "," +
               Mathf.RoundToInt(basePosition.y * 10f) + "," +
               Mathf.RoundToInt(basePosition.z * 10f);
    }

    public static bool TryFind(string key, out FireAlarmInteractable alarm)
    {
        if (!string.IsNullOrEmpty(key) && Registry.TryGetValue(key, out alarm) && alarm != null)
        {
            return true;
        }
        alarm = null;
        return false;
    }

    protected override string GetPromptText()
    {
        return (activated || pending) ? null : "Press E   Pull fire alarm";
    }

    protected override void OnInteract()
    {
        if (activated || pending)
        {
            return;
        }

        // Stop this alarm re-triggering while the request is in flight. The AUTHORITATIVE,
        // team-wide "activated" state comes back through ApplyActivated (via the coordinator),
        // which dedups so an alarm is only ever counted once for the whole team. In solo the
        // coordinator applies it immediately.
        pending = true;
        FireAlarmEasterEgg.RequestActivate(this);
    }

    /// <summary>
    /// Mark this alarm activated on THIS peer. Called by the coordinator on every peer when the
    /// server confirms the pull (and directly in solo / late-join catch-up). Idempotent.
    /// Visual state of the prop is left unchanged (no gameplay side effects); the prompt simply
    /// stops showing once the alarm is activated.
    /// </summary>
    public void ApplyActivated()
    {
        activated = true;
        pending = true;
    }
}
