namespace HorizonsAI.Models;

public class AppSettings
{
    public string       OpenRouterApiKey     { get; set; } = "";
    public string       DefaultModel         { get; set; } = "openai/gpt-4o-mini";
    public string       SpeakerName          { get; set; } = "Player";
    public string       AuthorsNote          { get; set; } = "";
    public VoiceProfile NarratorVoiceProfile { get; set; } = new();

    // Narrator / GM
    public bool   NarratorEnabled      { get; set; } = false;
    public string NarratorModel        { get; set; } = "";
    public string NarratorSystemPrompt { get; set; } = DefaultNarratorPrompt;

    public const string DefaultNarratorPrompt =
        "You are the Game Master for this collaborative roleplay session. " +
        "You manage the world — atmosphere, NPC entrances and exits, scene events, and challenge difficulty. " +
        "You do NOT speak for NPCs or control what they say; they speak for themselves.\n\n" +
        "After each exchange, decide:\n" +
        "- Write brief narration (1-3 sentences of atmosphere, world events, or scene transitions), " +
        "or set narration to null if nothing meaningful adds to the scene.\n" +
        "- Introduce new NPCs if the scene calls for it. Generate a fitting personality for each.\n" +
        "- Remove NPCs who have clearly left.\n" +
        "- Set the scene difficulty DC (5-20). Easy tasks: 8. Routine: 10. Moderate: 12. Hard: 15. Heroic: 18. " +
        "Update only when the tension or challenge level changes. Omit if unchanged.\n" +
        "- Set difficulty as 'easy', 'normal', or 'hard' based on how favourable conditions are for the player.\n\n" +
        "Respond ONLY in this exact JSON format (no other text):\n" +
        "{\n" +
        "  \"narration\": \"...\",\n" +
        "  \"dc\": 12,\n" +
        "  \"difficulty\": \"normal\",\n" +
        "  \"add\": [{ \"name\": \"...\", \"personality\": \"...\" }],\n" +
        "  \"remove\": []\n" +
        "}";
}
