/// <summary>
/// The perks available in School Of The Dead (Call of Duty Zombies style).
/// Each value maps to one perk machine and one gameplay effect applied by the
/// <see cref="PerkManager"/>.
/// </summary>
public enum PerkType
{
    /// <summary>Juggernog: raises maximum health and heals to full.</summary>
    Juggernog,

    /// <summary>Speed Cola: faster weapon reloads.</summary>
    SpeedCola,

    /// <summary>Double Tap: faster weapon fire rate.</summary>
    DoubleTap,

    /// <summary>Quick Revive: faster revive / enables a solo self-revive.</summary>
    QuickRevive,

    /// <summary>Stamin-Up: faster movement.</summary>
    StaminUp,

    /// <summary>Mule Kick: allows carrying one extra weapon.</summary>
    MuleKick,
}
