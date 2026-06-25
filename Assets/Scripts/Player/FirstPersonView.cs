using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// First-person view fix for the local player: hides the player's OWN character
/// body from their camera (so you never see the inside of your model or it
/// swinging into view), while still letting the body cast a shadow. Other players
/// in co-op still see your full model (that is handled by NetworkPlayerAvatar).
///
/// Put this on the player root; it hides the renderers under the "Model" child.
/// </summary>
public class FirstPersonView : MonoBehaviour
{
    [Tooltip("The character model to hide from this player's own camera. Auto-found ('Model' child) if left empty.")]
    public Transform modelRoot;

    private void Start()
    {
        Transform model = modelRoot != null ? modelRoot : transform.Find("Model");
        if (model == null)
        {
            model = transform;
        }

        // ShadowsOnly: the body is invisible to the camera but still casts a shadow,
        // so the world feels grounded without the body clipping into the view.
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }
}
