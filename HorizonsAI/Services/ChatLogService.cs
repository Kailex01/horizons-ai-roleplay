using HorizonsAI.Models;

namespace HorizonsAI.Services;

public static class ChatLogService
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    private static string LogPath(string key)
        => Path.Combine(AppConfig.ChatLogsFolder, $"{SanitizeKey(key)}.json");

    public static ConversationState Load(string key)
    {
        var path = LogPath(key);
        if (!File.Exists(path)) return new ConversationState();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ConversationState>(json) ?? new ConversationState();
        }
        catch { return new ConversationState(); }
    }

    public static void Save(string key, ConversationState state)
    {
        Directory.CreateDirectory(AppConfig.ChatLogsFolder);
        File.WriteAllText(LogPath(key), JsonSerializer.Serialize(state, _opts));
    }

    public static void Delete(string key)
    {
        var path = LogPath(key);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string SanitizeKey(string key)
        => string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));
}
