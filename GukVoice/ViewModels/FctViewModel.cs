using GukVoice.Models;

namespace GukVoice.ViewModels;

public enum FctCategory
{
    DamageOut, DamageIn,
    CritOut,   CritIn,
    SpellOut,  SpellIn,
    HealFriendly, HealEnemy,
    LevelUp,   ExpGain,
}

public record FctSpawnArgs(FctCategory Category, string Text);

public class FctViewModel : INotifyPropertyChanged
{
    public event Action<FctSpawnArgs>? SpawnRequested;

    // ── Per-category toggles (bound to FCT tab checkboxes) ────────────────────

    private FctSettings S => AppConfig.Current.Fct;

    public bool Enabled
    {
        get => S.Enabled;
        set { S.Enabled = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowDamageOut
    {
        get => S.ShowDamageOut;
        set { S.ShowDamageOut = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowDamageIn
    {
        get => S.ShowDamageIn;
        set { S.ShowDamageIn = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowCritOut
    {
        get => S.ShowCritOut;
        set { S.ShowCritOut = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowCritIn
    {
        get => S.ShowCritIn;
        set { S.ShowCritIn = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowSpellOut
    {
        get => S.ShowSpellOut;
        set { S.ShowSpellOut = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowSpellIn
    {
        get => S.ShowSpellIn;
        set { S.ShowSpellIn = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowHealFriendly
    {
        get => S.ShowHealFriendly;
        set { S.ShowHealFriendly = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowHealEnemy
    {
        get => S.ShowHealEnemy;
        set { S.ShowHealEnemy = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowLevelUp
    {
        get => S.ShowLevelUp;
        set { S.ShowLevelUp = value; Save(); OnPropertyChanged(); }
    }

    public bool ShowExpGain
    {
        get => S.ShowExpGain;
        set { S.ShowExpGain = value; Save(); OnPropertyChanged(); }
    }

    // ── Event routing ──────────────────────────────────────────────────────────

    public void ProcessEvent(CombatEvent ev)
    {
        if (!S.Enabled) return;

        var (category, text) = ev.Type switch
        {
            CombatEventType.DamageDealt when ev.Source == DamageSource.Melee
                => (FctCategory.DamageOut, $"{ev.Damage:N0}"),

            CombatEventType.DamageTaken when ev.Source == DamageSource.Melee
                => (FctCategory.DamageIn, $"{ev.Damage:N0}"),

            CombatEventType.CritDealt
                => (FctCategory.CritOut, $"{ev.Damage:N0}"),

            CombatEventType.CritTaken
                => (FctCategory.CritIn, $"{ev.Damage:N0}"),

            CombatEventType.DamageDealt when ev.Source == DamageSource.Spell
                => (FctCategory.SpellOut, $"{ev.Damage:N0}"),

            CombatEventType.DamageTaken when ev.Source == DamageSource.Spell
                => (FctCategory.SpellIn, $"{ev.Damage:N0}"),

            CombatEventType.HealDealt or CombatEventType.HealTaken
                => (FctCategory.HealFriendly, $"+{ev.Damage:N0}"),

            CombatEventType.HealEnemy
                => (FctCategory.HealEnemy, $"+{ev.Damage:N0}"),

            CombatEventType.LevelUp
                => (FctCategory.LevelUp, string.IsNullOrEmpty(ev.Label) ? "DING!" : $"DING! {ev.Label}"),

            CombatEventType.ExperienceGain
                => (FctCategory.ExpGain, "exp"),

            _ => (default, null),
        };

        if (text == null) return;
        if (!IsCategoryEnabled(category)) return;

        SpawnRequested?.Invoke(new FctSpawnArgs(category, text));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private bool IsCategoryEnabled(FctCategory cat) => cat switch
    {
        FctCategory.DamageOut    => S.ShowDamageOut,
        FctCategory.DamageIn     => S.ShowDamageIn,
        FctCategory.CritOut      => S.ShowCritOut,
        FctCategory.CritIn       => S.ShowCritIn,
        FctCategory.SpellOut     => S.ShowSpellOut,
        FctCategory.SpellIn      => S.ShowSpellIn,
        FctCategory.HealFriendly => S.ShowHealFriendly,
        FctCategory.HealEnemy    => S.ShowHealEnemy,
        FctCategory.LevelUp      => S.ShowLevelUp,
        FctCategory.ExpGain      => S.ShowExpGain,
        _                        => false,
    };

    private static void Save() => AppConfig.Save();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
