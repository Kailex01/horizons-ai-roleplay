using System.Globalization;
using System.Text.RegularExpressions;
using GukVoice.Models;

namespace GukVoice.Services;

public static class EqLogParser
{
    // ── Line wrapper ───────────────────────────────────────────────────────────
    private static readonly Regex RxLine = new(
        @"^\[(\w{3} \w{3} [ \d]\d \d{2}:\d{2}:\d{2} \d{4})\] (.+)$",
        RegexOptions.Compiled);

    // ── Chat patterns ──────────────────────────────────────────────────────────
    private static readonly Regex RxChatOther = new(
        @"^(.+?) (?:says?|shouts?|tells? you|tells? the group|says? out of character),? '(.+)'$",
        RegexOptions.Compiled);

    private static readonly Regex RxChatYou = new(
        @"^You (?:say|shout|tell|say out of character|tell the group),? '(.+)'$",
        RegexOptions.Compiled);

    // ── Event patterns ─────────────────────────────────────────────────────────
    private static readonly Regex RxZone = new(
        @"^You have entered (.+)\.$", RegexOptions.Compiled);

    private static readonly Regex RxExp = new(
        @"^You (?:have )?gained (?:\(\d+\) )?(?:party |raid )?experience",
        RegexOptions.Compiled);

    private static readonly Regex RxLoot = new(
        @"^--(.+?) has looted (.+?) from .+", RegexOptions.Compiled);

    private static readonly Regex RxLootYou = new(
        @"^You receive .+? from", RegexOptions.Compiled);

    // ── Combat — melee damage dealt ────────────────────────────────────────────
    private static readonly Regex RxDmgDealtMelee = new(
        @"^You (?:slash|pierce|crush|kick|punch|bite|bash|backstab|strike|claw|maul|gore|rend|frenzy on) (.+?) for (\d+) points? of damage\.$",
        RegexOptions.Compiled);

    // Combat — spell / non-melee damage dealt (standard EQ: "You hit")
    private static readonly Regex RxDmgDealtSpell = new(
        @"^You hit (.+?) for (\d+) points? of non-melee damage\.$",
        RegexOptions.Compiled);

    // Combat — melee damage taken ("YOU" is uppercase in EQ logs)
    private static readonly Regex RxDmgTakenMelee = new(
        @"^(.+?) (?:slash|pierce|crush|kick|punch|bite|bash|backstab|hit|strike|claw|maul|gore|rend)s? YOU for (\d+) points? of damage\.$",
        RegexOptions.Compiled);

    // Combat — spell damage taken (standard EQ: "X hit you")
    private static readonly Regex RxDmgTakenSpell = new(
        @"^(.+?) hit you for (\d+) points? of non-melee damage\.$",
        RegexOptions.Compiled);

    // Combat — third-person non-melee: "ActorName hit TargetName for N points of non-melee damage."
    // Some EQEmu servers emit this format for everyone (including the player) instead of "You hit".
    // Routed to SpellOut / SpellIn based on whether actor or target matches the player name.
    private static readonly Regex RxNonMeleeThirdPerson = new(
        @"^(.+?) hit (.+?) for (\d+) points? of non-melee damage\.$",
        RegexOptions.Compiled);

    // ── Combat — crits (separate log lines that follow the hit) ───────────────
    // Melee / archery / throwing crits dealt
    private static readonly Regex RxCritDealt = new(
        @"^You (?:score a critical hit|land a Crippling Blow|perform a Deadly Strike|scored a Finishing Blow upon|fire an arrow with tremendous force)[!.]?\((\d+)\)$",
        RegexOptions.Compiled);

    // Spell crits dealt
    private static readonly Regex RxCritDealtSpell = new(
        @"^You deliver a critical blast[!.]?\s*\((\d+)\)$",
        RegexOptions.Compiled);

    // Crits taken from NPCs — "NPC scores a critical hit! (1234)"
    private static readonly Regex RxCritTaken = new(
        @"^(.+?) (?:scores? a critical hit|lands? a Crippling Blow|performs? a Deadly Strike)[!.]?\((\d+)\)$",
        RegexOptions.Compiled);

    // ── Combat — heals ────────────────────────────────────────────────────────
    // You healed a friendly target: "You healed Soandso for 500 hit points."
    //   or with overheal: "You healed Soandso for 500 (750) hit points by Complete Heal."
    private static readonly Regex RxHealDealt = new(
        @"^You healed (.+?) for (\d+)",
        RegexOptions.Compiled);

    // You were healed: "You have been healed for 500 hit points."
    private static readonly Regex RxHealTakenPassive = new(
        @"^You have been healed for (\d+)",
        RegexOptions.Compiled);

    // Someone healed you: "Cleric healed you for 500 (750) hit points by Complete Heal."
    private static readonly Regex RxHealTakenActive = new(
        @"^(.+?) healed you for (\d+)",
        RegexOptions.Compiled);

    // Enemy healed: "Goblin has been healed for 500 hit points." (passive, non-you subject)
    private static readonly Regex RxHealEnemy = new(
        @"^(.+?) has been healed for (\d+)",
        RegexOptions.Compiled);

    // ── Combat — kills ────────────────────────────────────────────────────────
    private static readonly Regex RxYouSlew = new(
        @"^You have slain (.+)!$", RegexOptions.Compiled);

    private static readonly Regex RxSlainBy = new(
        @"^(.+?) has been slain by (.+?)!$", RegexOptions.Compiled);

    private static readonly Regex RxPlayerDied = new(
        @"^You have been slain by (.+?)!$", RegexOptions.Compiled);

    // ── Level up ──────────────────────────────────────────────────────────────
    // "You have gained a level! Welcome to level 55!"
    // "Congratulations! You have reached level 55!"
    private static readonly Regex RxLevelUp = new(
        @"(?:gained a level|reached level)\D*(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Public entry point ─────────────────────────────────────────────────────

    public record ParseResult(LogEvent? LogEvent, CombatEvent? CombatEvent);

    public static ParseResult Parse(string rawLine, string playerName,
                                     HashSet<string>? groupMembers = null)
    {
        var m = RxLine.Match(rawLine);
        if (!m.Success) return new(null, null);

        var time = ParseTimestamp(m.Groups[1].Value);
        var body = m.Groups[2].Value;

        // Run both parsers — some lines (exp, level-up) may return both so the
        // activity feed and FCT overlay both get notified.
        var combat   = TryCombat(body, time, playerName, groupMembers);
        var logEvent = TryLogEvent(body, time, playerName);
        return new(logEvent, combat);
    }

    // ── Log event parsing ──────────────────────────────────────────────────────

    private static LogEvent? TryLogEvent(string body, DateTime time, string playerName)
    {
        Match m;

        m = RxZone.Match(body);
        if (m.Success)
            return new LogEvent { Type = LogEventType.Zone, Time = time, Text = body };

        if (RxExp.IsMatch(body))
            return new LogEvent { Type = LogEventType.Experience, Time = time, Text = body };

        m = RxLoot.Match(body);
        if (m.Success)
            return new LogEvent { Type = LogEventType.Loot, Time = time, Text = body };

        if (RxLootYou.IsMatch(body))
            return new LogEvent { Type = LogEventType.Loot, Time = time, Text = body };

        m = RxChatOther.Match(body);
        if (m.Success)
            return new LogEvent { Type = LogEventType.Chat, Time = time,
                                  Speaker = m.Groups[1].Value, Text = m.Groups[2].Value };

        m = RxChatYou.Match(body);
        if (m.Success && !string.IsNullOrWhiteSpace(playerName))
            return new LogEvent { Type = LogEventType.Chat, Time = time,
                                  Speaker = playerName, Text = m.Groups[1].Value };

        return null;
    }

    // ── Combat parsing ─────────────────────────────────────────────────────────

    private static CombatEvent? TryCombat(string body, DateTime time, string playerName,
                                            HashSet<string>? groupMembers = null)
    {
        Match m;

        // ── Level up ───────────────────────────────────────────────────────────
        m = RxLevelUp.Match(body);
        if (m.Success)
        {
            var label = m.Groups[1].Success ? $"Level {m.Groups[1].Value}!" : "";
            return new CombatEvent { Type = CombatEventType.LevelUp, Time = time, Label = label };
        }

        // ── Experience gain ────────────────────────────────────────────────────
        if (RxExp.IsMatch(body))
            return new CombatEvent { Type = CombatEventType.ExperienceGain, Time = time };

        // ── Player death (check before RxSlainBy to avoid "You" false match) ──
        m = RxPlayerDied.Match(body);
        if (m.Success)
            return new CombatEvent { Type = CombatEventType.PlayerDeath, Time = time,
                                     Actor = m.Groups[1].Value };

        // ── Mob death ─────────────────────────────────────────────────────────
        m = RxYouSlew.Match(body);
        if (m.Success)
            return new CombatEvent { Type = CombatEventType.MobDeath, Time = time,
                                     Target = m.Groups[1].Value };

        m = RxSlainBy.Match(body);
        if (m.Success)
            return new CombatEvent { Type = CombatEventType.MobDeath, Time = time,
                                     Target = m.Groups[1].Value, Actor = m.Groups[2].Value };

        // ── Crits (check before regular hits — crits are separate log lines) ──
        m = RxCritDealt.Match(body);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int cd))
            return new CombatEvent { Type = CombatEventType.CritDealt, Time = time,
                                     Damage = cd, Source = DamageSource.Melee };

        m = RxCritDealtSpell.Match(body);
        if (m.Success && int.TryParse(m.Groups[1].Value, out cd))
            return new CombatEvent { Type = CombatEventType.CritDealt, Time = time,
                                     Damage = cd, Source = DamageSource.Spell };

        m = RxCritTaken.Match(body);
        if (m.Success && int.TryParse(m.Groups[2].Value, out int ct))
            return new CombatEvent { Type = CombatEventType.CritTaken, Time = time,
                                     Actor = m.Groups[1].Value, Damage = ct };

        // ── Regular damage dealt ───────────────────────────────────────────────
        m = RxDmgDealtMelee.Match(body);
        if (m.Success && int.TryParse(m.Groups[2].Value, out int dmg))
            return new CombatEvent { Type = CombatEventType.DamageDealt, Time = time,
                                     Target = m.Groups[1].Value, Damage = dmg, Source = DamageSource.Melee };

        m = RxDmgDealtSpell.Match(body);
        if (m.Success && int.TryParse(m.Groups[2].Value, out dmg))
            return new CombatEvent { Type = CombatEventType.DamageDealt, Time = time,
                                     Target = m.Groups[1].Value, Damage = dmg, Source = DamageSource.Spell };

        // ── Regular damage taken ───────────────────────────────────────────────
        m = RxDmgTakenMelee.Match(body);
        if (m.Success && int.TryParse(m.Groups[2].Value, out dmg))
            return new CombatEvent { Type = CombatEventType.DamageTaken, Time = time,
                                     Actor = m.Groups[1].Value, Damage = dmg, Source = DamageSource.Melee };

        m = RxDmgTakenSpell.Match(body);
        if (m.Success && int.TryParse(m.Groups[2].Value, out dmg))
            return new CombatEvent { Type = CombatEventType.DamageTaken, Time = time,
                                     Actor = m.Groups[1].Value, Damage = dmg, Source = DamageSource.Spell };

        // Third-person non-melee ("Jarvill hit Mob for N" / "Mob hit Jarvill for N")
        // Only runs when a player name is known; ignores other-player-vs-mob lines.
        if (!string.IsNullOrEmpty(playerName))
        {
            m = RxNonMeleeThirdPerson.Match(body);
            if (m.Success && int.TryParse(m.Groups[3].Value, out dmg))
            {
                var actor  = m.Groups[1].Value;
                var target = m.Groups[2].Value;
                if (actor == playerName && target != playerName)
                    return new CombatEvent { Type = CombatEventType.DamageDealt, Time = time,
                                             Target = target, Damage = dmg, Source = DamageSource.Spell };
                if (target == playerName)
                    return new CombatEvent { Type = CombatEventType.DamageTaken, Time = time,
                                             Actor = actor, Damage = dmg, Source = DamageSource.Spell };
                // Enabled group member hitting a mob → show as SpellOut
                if (groupMembers != null && groupMembers.Contains(actor) && target != playerName)
                    return new CombatEvent { Type = CombatEventType.DamageDealt, Time = time,
                                             Actor = actor, Target = target, Damage = dmg,
                                             Source = DamageSource.Spell };
            }
        }

        // ── Heals ─────────────────────────────────────────────────────────────
        m = RxHealDealt.Match(body);
        if (m.Success && int.TryParse(m.Groups[2].Value, out int heal))
            return new CombatEvent { Type = CombatEventType.HealDealt, Time = time,
                                     Target = m.Groups[1].Value, Damage = heal };

        m = RxHealTakenPassive.Match(body);
        if (m.Success && int.TryParse(m.Groups[1].Value, out heal))
            return new CombatEvent { Type = CombatEventType.HealTaken, Time = time, Damage = heal };

        m = RxHealTakenActive.Match(body);
        if (m.Success && int.TryParse(m.Groups[2].Value, out heal))
            return new CombatEvent { Type = CombatEventType.HealTaken, Time = time,
                                     Actor = m.Groups[1].Value, Damage = heal };

        // "X has been healed" — enemy heal (only reaches here because "You have been healed"
        // matched RxHealTakenPassive above)
        m = RxHealEnemy.Match(body);
        if (m.Success && int.TryParse(m.Groups[2].Value, out heal))
            return new CombatEvent { Type = CombatEventType.HealEnemy, Time = time,
                                     Target = m.Groups[1].Value, Damage = heal };

        return null;
    }

    // ── Timestamp ──────────────────────────────────────────────────────────────

    private static DateTime ParseTimestamp(string ts)
    {
        var s = Regex.Replace(ts, @"\s+", " ").Trim();
        if (DateTime.TryParseExact(s, "ddd MMM d HH:mm:ss yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        return DateTime.Now;
    }
}
