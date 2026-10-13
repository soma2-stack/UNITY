using TMPro;
using UnityEngine;

/// <summary>
/// Marker + authoring data placed on each editable DeathCinematic monitor object (Monitor_01..05).
/// Holds the references <see cref="DeathCinematicSceneController"/> needs to drive that monitor's
/// flicker/feed at runtime, so you can move, rename, or restyle the monitor freely as long as
/// these two fields stay wired.
///
/// TOP-LEVEL ON PURPOSE: this was previously a NESTED MonoBehaviour inside
/// DeathCinematicSceneController. A nested MonoBehaviour serialized onto scene objects corrupted
/// the DeathCinematic scene in standalone player builds ("level2 is corrupted! Position out of
/// bounds!"), so it lives in its own file with its own script GUID for reliable serialization.
/// </summary>
public sealed class DeathCinematicMonitor : MonoBehaviour
{
    [Tooltip("The CRT screen quad whose material colour is driven for the glow/flicker/static.")]
    public Renderer screenRenderer;
    [Tooltip("The feed label (camera name) shown on this monitor.")]
    public TMP_Text feedLabel;
}
