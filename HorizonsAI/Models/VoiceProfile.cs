namespace HorizonsAI.Models;

public class VoiceProfile
{
    [JsonPropertyName("voices")]          public List<VoiceWeight> Voices         { get; set; } = new();
    [JsonPropertyName("speed")]           public float             Speed          { get; set; } = 1.0f;
    [JsonPropertyName("pitch_semitones")] public float             PitchSemitones { get; set; } = 0.0f;
    [JsonPropertyName("tempo")]           public float             Tempo          { get; set; } = 1.0f;
    [JsonPropertyName("volume")]          public float             Volume         { get; set; } = 1.0f;

    [JsonIgnore] public bool IsEnabled => Voices.Count > 0 && Voices.Any(v => !string.IsNullOrWhiteSpace(v.Voice));
}
