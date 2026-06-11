namespace HorizonsAI.Models;

public class Character
{
    [JsonPropertyName("id")]            public string  Id           { get; set; } = "";
    [JsonPropertyName("name")]          public string  Name         { get; set; } = "";
    [JsonPropertyName("category")]      public string  Category     { get; set; } = "npcs";
    [JsonPropertyName("enabled")]       public bool    Enabled      { get; set; } = true;
    [JsonPropertyName("system_prompt")] public string  SystemPrompt { get; set; } = "";
    [JsonPropertyName("model")]         public string  Model        { get; set; } = "";
    [JsonPropertyName("voice_profile")]  public VoiceProfile VoiceProfile { get; set; } = new();
    [JsonPropertyName("portrait")]      public string? Portrait     { get; set; }

    [JsonIgnore] public string DisplayName   => Name;
    [JsonIgnore] public string CategoryBadge => Category.Replace('_', ' ').ToUpper();

    public static string MakeId(string name) =>
        name.ToLowerInvariant().Replace(' ', '_').Replace("'", "").Replace("\"", "");
}
