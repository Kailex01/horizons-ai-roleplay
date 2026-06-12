namespace HorizonsAI.Models;

public class Scene
{
    [JsonPropertyName("id")]      public string Id      { get; set; } = Guid.NewGuid().ToString("N")[..8];
    [JsonPropertyName("name")]    public string Name    { get; set; } = "";
    [JsonPropertyName("context")] public string Context { get; set; } = "";
    [JsonPropertyName("enabled")] public bool   Enabled { get; set; } = true;
}
