namespace HorizonsAI.Models;

public class ChatMessageDto
{
    [JsonPropertyName("text")]         public string   Text         { get; set; } = "";
    [JsonPropertyName("isPlayer")]     public bool     IsPlayer     { get; set; }
    [JsonPropertyName("isSummary")]    public bool     IsSummary    { get; set; }
    [JsonPropertyName("senderName")]   public string   SenderName   { get; set; } = "";
    [JsonPropertyName("portraitFile")] public string?  PortraitFile { get; set; }
    [JsonPropertyName("timestamp")]    public DateTime Timestamp    { get; set; }
}
