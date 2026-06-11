namespace HorizonsAI.Models;

public class ConversationState
{
    [JsonPropertyName("memory")]   public string?             Memory   { get; set; }
    [JsonPropertyName("messages")] public List<ChatMessageDto> Messages { get; set; } = new();
}
