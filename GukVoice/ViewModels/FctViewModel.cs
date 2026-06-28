using System.Windows.Media;
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
    public event Action<bool>?        EnabledChanged;
    public event Action?              OriginChanged;

    private FctSettings S => AppConfig.Current.Fct;

    // ── Master toggle ─────────────────────────────────────────────────────────

    public bool Enabled
    {
        get => S.Enabled;
        set { S.Enabled = value; Save(); OnPropertyChanged(); EnabledChanged?.Invoke(value); }
    }

    // ── Category visibility toggles ───────────────────────────────────────────

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

    // ── Origin / debug ────────────────────────────────────────────────────────

    public int OriginOffsetX
    {
        get => S.OriginOffsetX;
        set { S.OriginOffsetX = value; Save(); OnPropertyChanged(); OriginChanged?.Invoke(); }
    }
    public int OriginOffsetY
    {
        get => S.OriginOffsetY;
        set { S.OriginOffsetY = value; Save(); OnPropertyChanged(); OriginChanged?.Invoke(); }
    }
    public bool ShowDebugOrigin
    {
        get => S.ShowDebugOrigin;
        set { S.ShowDebugOrigin = value; Save(); OnPropertyChanged(); OriginChanged?.Invoke(); }
    }

    // ── Per-category font sizes ───────────────────────────────────────────────

    public double FontSizeDamageOut
    {
        get => S.FontSizeDamageOut;
        set { S.FontSizeDamageOut = value; Save(); OnPropertyChanged(); }
    }
    public double FontSizeDamageIn
    {
        get => S.FontSizeDamageIn;
        set { S.FontSizeDamageIn = value; Save(); OnPropertyChanged(); }
    }
    public double FontSizeCritOut
    {
        get => S.FontSizeCritOut;
        set { S.FontSizeCritOut = value; Save(); OnPropertyChanged(); }
    }
    public double FontSizeCritIn
    {
        get => S.FontSizeCritIn;
        set { S.FontSizeCritIn = value; Save(); OnPropertyChanged(); }
    }
    public double FontSizeSpellOut
    {
        get => S.FontSizeSpellOut;
        set { S.FontSizeSpellOut = value; Save(); OnPropertyChanged(); }
    }
    public double FontSizeSpellIn
    {
        get => S.FontSizeSpellIn;
        set { S.FontSizeSpellIn = value; Save(); OnPropertyChanged(); }
    }
    public double FontSizeHealFriendly
    {
        get => S.FontSizeHealFriendly;
        set { S.FontSizeHealFriendly = value; Save(); OnPropertyChanged(); }
    }
    public double FontSizeHealEnemy
    {
        get => S.FontSizeHealEnemy;
        set { S.FontSizeHealEnemy = value; Save(); OnPropertyChanged(); }
    }
    public double FontSizeLevelUp
    {
        get => S.FontSizeLevelUp;
        set { S.FontSizeLevelUp = value; Save(); OnPropertyChanged(); }
    }
    public double FontSizeExpGain
    {
        get => S.FontSizeExpGain;
        set { S.FontSizeExpGain = value; Save(); OnPropertyChanged(); }
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

    // ── Per-category colors ───────────────────────────────────────────────────

    // ── Fill color brushes ────────────────────────────────────────────────────
    public SolidColorBrush BrushDamageOut    => MakeBrush(S.ColorDamageOut);
    public SolidColorBrush BrushDamageIn     => MakeBrush(S.ColorDamageIn);
    public SolidColorBrush BrushCritOut      => MakeBrush(S.ColorCritOut);
    public SolidColorBrush BrushCritIn       => MakeBrush(S.ColorCritIn);
    public SolidColorBrush BrushSpellOut     => MakeBrush(S.ColorSpellOut);
    public SolidColorBrush BrushSpellIn      => MakeBrush(S.ColorSpellIn);
    public SolidColorBrush BrushHealFriendly => MakeBrush(S.ColorHealFriendly);
    public SolidColorBrush BrushHealEnemy    => MakeBrush(S.ColorHealEnemy);
    public SolidColorBrush BrushLevelUp      => MakeBrush(S.ColorLevelUp);
    public SolidColorBrush BrushExpGain      => MakeBrush(S.ColorExpGain);

    // ── Stroke color brushes ──────────────────────────────────────────────────
    public SolidColorBrush StrokeBrushDamageOut    => MakeBrush(S.StrokeDamageOut);
    public SolidColorBrush StrokeBrushDamageIn     => MakeBrush(S.StrokeDamageIn);
    public SolidColorBrush StrokeBrushCritOut      => MakeBrush(S.StrokeCritOut);
    public SolidColorBrush StrokeBrushCritIn       => MakeBrush(S.StrokeCritIn);
    public SolidColorBrush StrokeBrushSpellOut     => MakeBrush(S.StrokeSpellOut);
    public SolidColorBrush StrokeBrushSpellIn      => MakeBrush(S.StrokeSpellIn);
    public SolidColorBrush StrokeBrushHealFriendly => MakeBrush(S.StrokeHealFriendly);
    public SolidColorBrush StrokeBrushHealEnemy    => MakeBrush(S.StrokeHealEnemy);
    public SolidColorBrush StrokeBrushLevelUp      => MakeBrush(S.StrokeLevelUp);
    public SolidColorBrush StrokeBrushExpGain      => MakeBrush(S.StrokeExpGain);

    public string GetColorHex(FctCategory cat) => cat switch
    {
        FctCategory.DamageOut    => S.ColorDamageOut,
        FctCategory.DamageIn     => S.ColorDamageIn,
        FctCategory.CritOut      => S.ColorCritOut,
        FctCategory.CritIn       => S.ColorCritIn,
        FctCategory.SpellOut     => S.ColorSpellOut,
        FctCategory.SpellIn      => S.ColorSpellIn,
        FctCategory.HealFriendly => S.ColorHealFriendly,
        FctCategory.HealEnemy    => S.ColorHealEnemy,
        FctCategory.LevelUp      => S.ColorLevelUp,
        FctCategory.ExpGain      => S.ColorExpGain,
        _                        => "#FFFFFF",
    };

    public string GetStrokeHex(FctCategory cat) => cat switch
    {
        FctCategory.DamageOut    => S.StrokeDamageOut,
        FctCategory.DamageIn     => S.StrokeDamageIn,
        FctCategory.CritOut      => S.StrokeCritOut,
        FctCategory.CritIn       => S.StrokeCritIn,
        FctCategory.SpellOut     => S.StrokeSpellOut,
        FctCategory.SpellIn      => S.StrokeSpellIn,
        FctCategory.HealFriendly => S.StrokeHealFriendly,
        FctCategory.HealEnemy    => S.StrokeHealEnemy,
        FctCategory.LevelUp      => S.StrokeLevelUp,
        FctCategory.ExpGain      => S.StrokeExpGain,
        _                        => "#000000",
    };

    public void SetColor(FctCategory cat, string hex)
    {
        switch (cat)
        {
            case FctCategory.DamageOut:    S.ColorDamageOut    = hex; OnPropertyChanged(nameof(BrushDamageOut));    break;
            case FctCategory.DamageIn:     S.ColorDamageIn     = hex; OnPropertyChanged(nameof(BrushDamageIn));     break;
            case FctCategory.CritOut:      S.ColorCritOut      = hex; OnPropertyChanged(nameof(BrushCritOut));      break;
            case FctCategory.CritIn:       S.ColorCritIn       = hex; OnPropertyChanged(nameof(BrushCritIn));       break;
            case FctCategory.SpellOut:     S.ColorSpellOut     = hex; OnPropertyChanged(nameof(BrushSpellOut));     break;
            case FctCategory.SpellIn:      S.ColorSpellIn      = hex; OnPropertyChanged(nameof(BrushSpellIn));      break;
            case FctCategory.HealFriendly: S.ColorHealFriendly = hex; OnPropertyChanged(nameof(BrushHealFriendly)); break;
            case FctCategory.HealEnemy:    S.ColorHealEnemy    = hex; OnPropertyChanged(nameof(BrushHealEnemy));    break;
            case FctCategory.LevelUp:      S.ColorLevelUp      = hex; OnPropertyChanged(nameof(BrushLevelUp));      break;
            case FctCategory.ExpGain:      S.ColorExpGain      = hex; OnPropertyChanged(nameof(BrushExpGain));      break;
        }
        Save();
    }

    public void SetStroke(FctCategory cat, string hex)
    {
        switch (cat)
        {
            case FctCategory.DamageOut:    S.StrokeDamageOut    = hex; OnPropertyChanged(nameof(StrokeBrushDamageOut));    break;
            case FctCategory.DamageIn:     S.StrokeDamageIn     = hex; OnPropertyChanged(nameof(StrokeBrushDamageIn));     break;
            case FctCategory.CritOut:      S.StrokeCritOut      = hex; OnPropertyChanged(nameof(StrokeBrushCritOut));      break;
            case FctCategory.CritIn:       S.StrokeCritIn       = hex; OnPropertyChanged(nameof(StrokeBrushCritIn));       break;
            case FctCategory.SpellOut:     S.StrokeSpellOut     = hex; OnPropertyChanged(nameof(StrokeBrushSpellOut));     break;
            case FctCategory.SpellIn:      S.StrokeSpellIn      = hex; OnPropertyChanged(nameof(StrokeBrushSpellIn));      break;
            case FctCategory.HealFriendly: S.StrokeHealFriendly = hex; OnPropertyChanged(nameof(StrokeBrushHealFriendly)); break;
            case FctCategory.HealEnemy:    S.StrokeHealEnemy    = hex; OnPropertyChanged(nameof(StrokeBrushHealEnemy));    break;
            case FctCategory.LevelUp:      S.StrokeLevelUp      = hex; OnPropertyChanged(nameof(StrokeBrushLevelUp));      break;
            case FctCategory.ExpGain:      S.StrokeExpGain      = hex; OnPropertyChanged(nameof(StrokeBrushExpGain));      break;
        }
        Save();
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        try { return new((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new(Colors.White); }
    }

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
