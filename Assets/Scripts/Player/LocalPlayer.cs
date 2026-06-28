using UnityEngine;

/// <summary>
/// Tracks the LOCAL player's root so per-player systems (HUD, interactables, weapon /
/// perk grants, zombie targeting helpers) resolve THIS peer's player instead of an
/// arbitrary one. In multiplayer several players exist in the scene, so the old
/// FindFirstObjectByType&lt;CharacterController/WeaponController/...&gt;() pattern grabbed
/// whichever happened to be first — usually the wrong player.
///
/// In a networked session <see cref="NetworkPlayerAvatar"/> registers the OWNING player
/// on spawn and unregisters it on despawn. In solo (or before anything registers) every
/// getter falls back to FindFirstObjectByType, so single-player behaviour is unchanged.
/// </summary>
public static class LocalPlayer
{
    private static GameObject root;

    /// <summary>Register the local (owning) player's root GameObject.</summary>
    public static void Register(GameObject playerRoot)
    {
        root = playerRoot;
    }

    /// <summary>Clear the registration if it still points at this root.</summary>
    public static void Unregister(GameObject playerRoot)
    {
        if (root == playerRoot)
        {
            root = null;
        }
    }

    /// <summary>True once a local player has registered (i.e. a networked session).</summary>
    public static bool HasRegistered => root != null;

    public static Transform Transform
    {
        get
        {
            if (root != null)
            {
                return root.transform;
            }
            CharacterController controller = Object.FindFirstObjectByType<CharacterController>();
            return controller != null ? controller.transform : null;
        }
    }

    public static CharacterController Controller => Resolve<CharacterController>();
    public static PlayerHealth Health => Resolve<PlayerHealth>();
    public static WeaponController Weapon => Resolve<WeaponController>();
    public static PlayerMovement Movement => Resolve<PlayerMovement>();
    public static CoDCamera Camera => Resolve<CoDCamera>();

    // Prefer the registered local player's component; fall back to a scene search (solo).
    private static T Resolve<T>() where T : Component
    {
        if (root != null)
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
            {
                return component;
            }
        }
        return Object.FindFirstObjectByType<T>();
    }
}
