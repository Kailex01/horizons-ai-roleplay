namespace HorizonsAI.Models;

public class AppSettings
{
    public string OpenRouterApiKey   { get; set; } = "";
    public string DefaultModel       { get; set; } = "openai/gpt-4o-mini";
    public string SpeakerName        { get; set; } = "Player";
    public string TtsBackend         { get; set; } = "piper";
    public string PiperExePath       { get; set; } = "";
    public string PiperModelsPath    { get; set; } = "";
    public string NarratorVoiceModel { get; set; } = "";
    public string SpeachesBaseUrl    { get; set; } = "http://localhost:8880";
    public string AuthorsNote        { get; set; } = "";
}
