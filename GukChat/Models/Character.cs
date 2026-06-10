namespace GukChat.Models;

public class Character
{
    [JsonPropertyName("game")]          public string  Game         { get; set; } = "";
    [JsonPropertyName("category")]      public string  Category     { get; set; } = "";
    [JsonPropertyName("npc_name")]      public string  NpcName      { get; set; } = "";
    [JsonPropertyName("portrait_url")]  public string? PortraitUrl  { get; set; }
    [JsonPropertyName("voice_model")]   public string? VoiceModel   { get; set; }
    [JsonPropertyName("voice_speaker")] public int     VoiceSpeaker { get; set; }
    [JsonPropertyName("enabled")]       public bool    Enabled      { get; set; } = true;

    public string DisplayName   => NpcName.Replace('_', ' ');
    public string CategoryBadge => Category.ToUpper();
}
