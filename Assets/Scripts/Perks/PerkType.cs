/// <summary>
/// The perks available in School Of The Dead (Call of Duty Zombies style).
/// Each value maps to one perk machine and one gameplay effect applied by the
/// <see cref="PerkManager"/>.
/// </summary>
public enum PerkType
{
    /// <summary>Vital Boost: raises maximum health and heals to full.</summary>
    VitalBoost,

    /// <summary>Clip Kick: faster weapon reloads.</summary>
    ClipKick,

    /// <summary>Rapid Ruin: faster weapon fire rate.</summary>
    RapidRuin,

    /// <summary>Rescue Rush: faster revive / enables a solo self-revive.</summary>
    RescueRush,

    /// <summary>Sprint Surge: faster movement.</summary>
    SprintSurge,

    /// <summary>Armory Amp: allows carrying one extra weapon.</summary>
    ArmoryAmp,
}
