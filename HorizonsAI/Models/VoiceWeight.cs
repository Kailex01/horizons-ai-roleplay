namespace HorizonsAI.Models;

public class VoiceWeight
{
    [JsonPropertyName("voice")]  public string Voice  { get; set; } = "";
    [JsonPropertyName("weight")] public float  Weight { get; set; } = 1.0f;
}
