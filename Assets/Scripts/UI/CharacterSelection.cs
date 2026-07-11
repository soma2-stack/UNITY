using UnityEngine;

/// <summary>
/// Per-player character choice for "School of the Dead", stored locally in PlayerPrefs — the same
/// local-settings store the project already uses for the multiplayer display name. Each machine
/// keeps its own choice, so the host and every client can pick a different character.
///
/// The stored value is the PORTRAIT INDEX (0..3), i.e. which <c>player_portrait_N</c> file the
/// character uses. That is exactly what <see cref="SchoolOfTheDeadHud"/> loads, so the HUD needs no
/// change. Display order and the portrait each name uses are defined ONCE in <see cref="Characters"/>
/// so names and portraits can never get mismatched.
///
/// It is intentionally local-only and tiny: nothing here is networked and no gameplay is touched.
/// When no character has been chosen, callers fall back to the previous <c>OwnerClientId % Count</c>
/// behavior, so default spawning is unchanged.
/// </summary>
public static class CharacterSelection
{
    /// <summary>A selectable survivor: a display name paired with the portrait file it uses.</summary>
    public readonly struct Character
    {
        public readonly string DisplayName;
        public readonly int PortraitIndex;   // Resources/HUD/player_portrait_{PortraitIndex}

        public Character(string displayName, int portraitIndex)
        {
            DisplayName = displayName;
            PortraitIndex = portraitIndex;
        }

        public string PortraitResource => "HUD/player_portrait_" + PortraitIndex;
    }

    /// <summary>
    /// Survivors in UI display order (Steven, Maya, Tyler, Hank). The portrait file each one uses is
    /// EXPLICIT here so names and portraits stay in sync regardless of file ordering:
    ///   Steven → player_portrait_0, Maya → player_portrait_3, Tyler → player_portrait_2, Hank → player_portrait_1.
    /// </summary>
    public static readonly Character[] Characters =
    {
        new Character("Steven", 0),
        new Character("Maya",   3),
        new Character("Tyler",  2),
        new Character("Hank",   1),
    };

    /// <summary>Number of selectable characters (and portrait files, player_portrait_0 .. _3).</summary>
    public const int Count = 4;

    private const string PrefKey = "SelectedCharacterIndex";

    /// <summary>The chosen PORTRAIT index (0..3), or -1 if none has been chosen.</summary>
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

    /// <summary>Store the chosen PORTRAIT index (ignored if out of range). Persisted immediately.</summary>
    public static void Select(int portraitIndex)
    {
        if (portraitIndex < 0 || portraitIndex >= Count)
        {
            return;
        }
        PlayerPrefs.SetInt(PrefKey, portraitIndex);
        PlayerPrefs.Save();
    }

    /// <summary>Display name for a PORTRAIT index (looked up in the table), or "Survivor" if unknown.</summary>
    public static string NameOf(int portraitIndex)
    {
        foreach (Character character in Characters)
        {
            if (character.PortraitIndex == portraitIndex)
            {
                return character.DisplayName;
            }
        }
        return "Survivor";
    }
}
