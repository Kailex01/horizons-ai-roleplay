namespace GukChat.Models;

public class AppSettings
{
    public string BotAiBaseUrl    { get; set; } = "http://192.168.10.227:5001";
    public string SpeakerName     { get; set; } = "Aradune";
    public string PiperExePath    { get; set; } = "";
    public string PiperModelsPath { get; set; } = "";
}
