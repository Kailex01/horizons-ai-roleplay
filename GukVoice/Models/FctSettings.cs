namespace GukVoice.Models;

public class FctSettings
{
    [JsonPropertyName("enabled")]            public bool Enabled          { get; set; } = true;
    [JsonPropertyName("show_damage_out")]    public bool ShowDamageOut    { get; set; } = true;
    [JsonPropertyName("show_damage_in")]     public bool ShowDamageIn     { get; set; } = true;
    [JsonPropertyName("show_crit_out")]      public bool ShowCritOut      { get; set; } = true;
    [JsonPropertyName("show_crit_in")]       public bool ShowCritIn       { get; set; } = true;
    [JsonPropertyName("show_spell_out")]     public bool ShowSpellOut     { get; set; } = true;
    [JsonPropertyName("show_spell_in")]      public bool ShowSpellIn      { get; set; } = true;
    [JsonPropertyName("show_heal_friendly")] public bool ShowHealFriendly { get; set; } = true;
    [JsonPropertyName("show_heal_enemy")]    public bool ShowHealEnemy    { get; set; } = false;
    [JsonPropertyName("show_level_up")]      public bool ShowLevelUp      { get; set; } = true;
    [JsonPropertyName("show_exp_gain")]      public bool ShowExpGain      { get; set; } = false;

    // ── Origin offset (pixels from window center; positive = right / down) ────
    [JsonPropertyName("origin_x")]     public int  OriginOffsetX   { get; set; } = 0;
    [JsonPropertyName("origin_y")]     public int  OriginOffsetY   { get; set; } = 0;
    [JsonPropertyName("debug_origin")] public bool ShowDebugOrigin { get; set; } = false;

    // ── Per-category starting font sizes (px) ─────────────────────────────────
    [JsonPropertyName("fs_damage_out")]    public double FontSizeDamageOut    { get; set; } = 18;
    [JsonPropertyName("fs_damage_in")]     public double FontSizeDamageIn     { get; set; } = 18;
    [JsonPropertyName("fs_crit_out")]      public double FontSizeCritOut      { get; set; } = 26;
    [JsonPropertyName("fs_crit_in")]       public double FontSizeCritIn       { get; set; } = 26;
    [JsonPropertyName("fs_spell_out")]     public double FontSizeSpellOut     { get; set; } = 18;
    [JsonPropertyName("fs_spell_in")]      public double FontSizeSpellIn      { get; set; } = 18;
    [JsonPropertyName("fs_heal_friendly")] public double FontSizeHealFriendly { get; set; } = 18;
    [JsonPropertyName("fs_heal_enemy")]    public double FontSizeHealEnemy    { get; set; } = 16;
    [JsonPropertyName("fs_level_up")]      public double FontSizeLevelUp      { get; set; } = 30;
    [JsonPropertyName("fs_exp_gain")]      public double FontSizeExpGain      { get; set; } = 13;

    // ── Per-category fill colors (#RRGGBB) ───────────────────────────────────
    [JsonPropertyName("clr_damage_out")]    public string ColorDamageOut    { get; set; } = "#FFFFFF";
    [JsonPropertyName("clr_damage_in")]     public string ColorDamageIn     { get; set; } = "#FF7043";
    [JsonPropertyName("clr_crit_out")]      public string ColorCritOut      { get; set; } = "#FFD700";
    [JsonPropertyName("clr_crit_in")]       public string ColorCritIn       { get; set; } = "#FF3030";
    [JsonPropertyName("clr_spell_out")]     public string ColorSpellOut     { get; set; } = "#64B5F6";
    [JsonPropertyName("clr_spell_in")]      public string ColorSpellIn      { get; set; } = "#CE93D8";
    [JsonPropertyName("clr_heal_friendly")] public string ColorHealFriendly { get; set; } = "#81C784";
    [JsonPropertyName("clr_heal_enemy")]    public string ColorHealEnemy    { get; set; } = "#CDDC39";
    [JsonPropertyName("clr_level_up")]      public string ColorLevelUp      { get; set; } = "#FFD700";
    [JsonPropertyName("clr_exp_gain")]      public string ColorExpGain      { get; set; } = "#FFF59D";

    // ── Per-category stroke (outline) colors (#RRGGBB) ───────────────────────
    [JsonPropertyName("stk_damage_out")]    public string StrokeDamageOut    { get; set; } = "#000000";
    [JsonPropertyName("stk_damage_in")]     public string StrokeDamageIn     { get; set; } = "#000000";
    [JsonPropertyName("stk_crit_out")]      public string StrokeCritOut      { get; set; } = "#000000";
    [JsonPropertyName("stk_crit_in")]       public string StrokeCritIn       { get; set; } = "#000000";
    [JsonPropertyName("stk_spell_out")]     public string StrokeSpellOut     { get; set; } = "#000000";
    [JsonPropertyName("stk_spell_in")]      public string StrokeSpellIn      { get; set; } = "#000000";
    [JsonPropertyName("stk_heal_friendly")] public string StrokeHealFriendly { get; set; } = "#000000";
    [JsonPropertyName("stk_heal_enemy")]    public string StrokeHealEnemy    { get; set; } = "#000000";
    [JsonPropertyName("stk_level_up")]      public string StrokeLevelUp      { get; set; } = "#000000";
    [JsonPropertyName("stk_exp_gain")]      public string StrokeExpGain      { get; set; } = "#000000";
}
