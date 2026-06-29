namespace GukVoice.Models;

public enum CombatEventType
{
    DamageDealt,     // melee hit dealt
    DamageTaken,     // melee hit taken
    CritDealt,       // crit dealt (melee or spell — separate log line)
    CritTaken,       // crit taken
    HealDealt,       // you healed a friendly
    HealTaken,       // you were healed by a friendly
    HealEnemy,       // enemy was healed
    MobDeath,
    PlayerDeath,
    LevelUp,
    ExperienceGain,
    Stunned,
    Feared,
}

public enum DamageSource { Melee, Spell }

public class CombatEvent
{
    public CombatEventType Type   { get; init; }
    public DateTime        Time   { get; init; }
    public string          Actor  { get; init; } = "";
    public string          Target { get; init; } = "";
    public int             Damage { get; init; }
    public DamageSource    Source { get; init; }
    public string          Label  { get; init; } = "";   // e.g. "Level 55!" for LevelUp
}
