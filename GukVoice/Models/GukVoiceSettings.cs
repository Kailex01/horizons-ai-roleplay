namespace GukVoice.Models;

public class GukVoiceSettings
{
    [JsonPropertyName("eq_log_path")]        public string              EqLogPath        { get; set; } = "";
    [JsonPropertyName("player_name")]        public string              PlayerName       { get; set; } = "";
    [JsonPropertyName("archive_folder")]     public string              ArchiveFolder    { get; set; } = "";
    [JsonPropertyName("archive_on_eq_exit")] public bool                ArchiveOnEqExit  { get; set; } = true;
    [JsonPropertyName("speakers")]           public List<SpeakerProfile> Speakers        { get; set; } = new();
    [JsonPropertyName("zone_voice")]         public VoiceProfile?       ZoneVoice        { get; set; }
    [JsonPropertyName("exp_voice")]          public VoiceProfile?       ExpVoice         { get; set; }
    [JsonPropertyName("loot_voice")]         public VoiceProfile?       LootVoice        { get; set; }
    [JsonPropertyName("narrator_voice")]     public VoiceProfile?       NarratorVoice    { get; set; }
    [JsonPropertyName("fct")]               public FctSettings         Fct              { get; set; } = new();
}
