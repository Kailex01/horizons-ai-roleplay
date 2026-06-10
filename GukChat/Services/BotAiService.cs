using GukChat.Models;

namespace GukChat.Services;

public class BotAiService
{
    private readonly HttpClient _http;

    public BotAiService(HttpClient http) => _http = http;

    public async Task<List<Character>> GetCharactersAsync()
    {
        var list = await _http.GetFromJsonAsync<List<Character>>(
            $"{AppConfig.BotAiBaseUrl}/api/personalities?game={AppConfig.Game}") ?? [];

        foreach (var c in list.Where(c => c.PortraitUrl != null))
            c.PortraitUrl = AppConfig.BotAiBaseUrl + c.PortraitUrl;

        return list
            .Where(c => c.Enabled)
            .OrderBy(c => c.Category)
            .ThenBy(c => c.DisplayName)
            .ToList();
    }

    public async Task<BitmapImage?> GetPortraitAsync(string absoluteUrl)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(absoluteUrl);
            var bmp   = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption     = BitmapCacheOption.OnLoad;
            bmp.StreamSource    = new MemoryStream(bytes);
            bmp.DecodePixelWidth = 256;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public async Task<List<string>> ChatAsync(Character character, string message)
    {
        var body = new
        {
            game         = character.Game,
            category     = character.Category,
            npc_name     = character.NpcName,
            speaker_name = AppConfig.SpeakerName,
            location     = AppConfig.DefaultLocation,
            channel      = AppConfig.DefaultChannel,
            message,
            context      = new { },
        };
        var resp = await _http.PostAsJsonAsync($"{AppConfig.BotAiBaseUrl}/api/chat", body);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<ChatResponse>();
        return result?.Lines ?? [];
    }

    private record ChatResponse([property: JsonPropertyName("lines")] List<string> Lines);
}
