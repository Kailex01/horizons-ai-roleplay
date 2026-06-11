namespace HorizonsAI.Models;

public class ChatMessage
{
    public string       Text         { get; init; } = "";
    public bool         IsPlayer     { get; init; }
    public bool         IsSummary    { get; init; }
    public bool         IsCharacter  => !IsPlayer && !IsSummary;
    public string       SenderName   { get; init; } = "";
    public string?      PortraitFile { get; init; }
    public BitmapImage? Portrait     { get; init; }
    public DateTime     Timestamp    { get; init; } = DateTime.Now;
    public string       TimeStr      => Timestamp.ToString("HH:mm");
    public bool         HasPortrait  => Portrait != null;
}
