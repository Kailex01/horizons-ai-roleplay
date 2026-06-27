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
}
