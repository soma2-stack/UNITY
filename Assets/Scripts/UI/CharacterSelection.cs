using UnityEngine;

/// <summary>
/// Per-player character choice for "School of the Dead", stored locally in PlayerPrefs — the same
/// local-settings store the project already uses for the multiplayer display name. Each machine
/// keeps its own choice, so the host and every client can pick a different character.
///
/// The chosen index drives the in-game HUD portrait (see <see cref="SchoolOfTheDeadHud"/>). It is
/// intentionally local-only and tiny: nothing here is networked and no gameplay is touched. When
/// no character has been chosen, callers fall back to the previous <c>OwnerClientId % Count</c>
/// behavior, so default spawning is unchanged.
/// </summary>
public static class CharacterSelection
{
    /// <summary>Number of selectable characters (portraits player_portrait_0 .. _3).</summary>
    public const int Count = 4;

    private const string PrefKey = "SelectedCharacterIndex";

    /// <summary>The locally chosen character index (0..Count-1), or -1 if none has been chosen.</summary>
    public static int SelectedIndex
    {
        get
        {
            int value = PlayerPrefs.GetInt(PrefKey, -1);
            return (value >= 0 && value < Count) ? value : -1;
        }
    }

    /// <summary>True once the player has explicitly chosen a character.</summary>
    public static bool HasSelection => SelectedIndex >= 0;

    /// <summary>Store the chosen character (ignored if out of range). Persisted immediately.</summary>
    public static void Select(int index)
    {
        if (index < 0 || index >= Count)
        {
            return;
        }
        PlayerPrefs.SetInt(PrefKey, index);
        PlayerPrefs.Save();
    }
}
