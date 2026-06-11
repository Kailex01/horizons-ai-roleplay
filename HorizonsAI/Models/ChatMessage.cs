namespace HorizonsAI.Models;

public class ChatMessage
{
    public string       Text       { get; init; } = "";
    public bool         IsPlayer   { get; init; }
    public bool         IsCharacter => !IsPlayer;
    public string       SenderName { get; init; } = "";
    public BitmapImage? Portrait   { get; init; }
    public DateTime     Timestamp  { get; init; } = DateTime.Now;
    public string       TimeStr    => Timestamp.ToString("HH:mm");
    public bool         HasPortrait => Portrait != null;
}
