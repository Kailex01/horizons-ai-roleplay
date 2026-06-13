using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HorizonsAI.Models;

namespace HorizonsAI.Services;

public record NarratorResult(
    string?        Narration,
    List<SceneNpc> Add,
    List<string>   Remove,
    int?           Dc,
    string?        Difficulty);

public record CharacterDraft(string SystemPrompt, StatBlock Stats);

public class NarratorService
{
    private readonly HttpClient _http;

    public NarratorService(HttpClient http) => _http = http;

    public async Task<NarratorResult?> EvaluateAsync(
        IEnumerable<ChatMessage> history,
        IEnumerable<string> activeNpcNames,
        bool forceEnabled = false)
    {
        var settings = AppConfig.Current;
        if (!settings.NarratorEnabled && !forceEnabled) return null;

        var apiKey = settings.OpenRouterApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var model = string.IsNullOrWhiteSpace(settings.NarratorModel)
            ? settings.DefaultModel
            : settings.NarratorModel;

        var rosterLine = "Active scene participants: " + string.Join(", ", activeNpcNames);

        var historyText = string.Join("\n", history
            .TakeLast(15)
            .Select(m => m.IsSummary        ? $"[Earlier summary: {m.Text}]"
                       : m.IsNarratorAction ? $"*{m.Text}*"
                       : string.IsNullOrEmpty(m.SenderName) ? m.Text
                       : $"{m.SenderName}: {m.Text}"));

        var apiMessages = new List<object>
        {
            new { role = "system", content = settings.NarratorSystemPrompt + "\n\n" + rosterLine },
            new { role = "user",   content = "Recent scene:\n" + historyText + "\n\nEvaluate the scene now." },
        };

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://openrouter.ai/api/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("HTTP-Referer", "https://github.com/Kailex01/horizons-ai-roleplay");
            request.Headers.Add("X-Title", "Horizon's AI");
            request.Content = JsonContent.Create(new { model, messages = apiMessages, max_tokens = 500 });

            var resp = await _http.SendAsync(request);
            if (!resp.IsSuccessStatusCode) return null;

            var result = await resp.Content.ReadFromJsonAsync<OAIResponse>();
            var json   = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrEmpty(json)) return null;

            return ParseResult(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<CharacterDraft?> GenerateCharacterPromptAsync(
        string name, string personality, IEnumerable<ChatMessage> recentHistory)
    {
        var settings = AppConfig.Current;
        var apiKey   = settings.OpenRouterApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var model = string.IsNullOrWhiteSpace(settings.NarratorModel)
            ? settings.DefaultModel
            : settings.NarratorModel;

        var sceneContext = string.Join("\n", recentHistory
            .TakeLast(10)
            .Where(m => !m.IsSummary)
            .Select(m => m.IsNarratorAction
                ? $"*{m.Text}*"
                : string.IsNullOrEmpty(m.SenderName) ? m.Text : $"{m.SenderName}: {m.Text}"));

        var defaultPrompt = AppConfig.Current.DefaultCharacterPrompt;
        var worldContext  = string.IsNullOrWhiteSpace(defaultPrompt)
            ? ""
            : $"\n\nWorld context for this setting:\n{defaultPrompt}";

        var apiMessages = new List<object>
        {
            new { role = "system", content =
                "You create roleplay character profiles. Return ONLY a JSON object, no other text:\n" +
                "{\n" +
                "  \"prompt\": \"You are [Name]... (3-5 sentences: personality, speech style, motivations, role in scene)\",\n" +
                "  \"stats\": { \"str\": 10, \"dex\": 10, \"con\": 10, \"int\": 10, \"wis\": 10, \"cha\": 10, \"hp\": 10, \"ac\": 10 }\n" +
                "}\n" +
                "Stats should reflect the character (average human=10, ability range 6-18). " +
                "hp = max hit points (commoner ~8, guard ~11, veteran ~52). " +
                "ac = armor class (unarmored ~10, leather ~11, chain ~14, plate ~18). " +
                "A barkeep: CON 13, CHA 12, hp 9, ac 10. A guard: STR 13, CON 12, hp 11, ac 16. Etc." +
                worldContext },
            new { role = "user", content =
                $"Name: {name}\n" +
                $"Personality sketch: {personality}\n\n" +
                $"Recent scene context:\n{sceneContext}\n\n" +
                "Generate the character profile JSON." },
        };

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://openrouter.ai/api/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("HTTP-Referer", "https://github.com/Kailex01/horizons-ai-roleplay");
            request.Headers.Add("X-Title", "Horizon's AI");
            request.Content = JsonContent.Create(new { model, messages = apiMessages, max_tokens = 400 });

            var resp = await _http.SendAsync(request);
            if (!resp.IsSuccessStatusCode) return null;

            var result  = await resp.Content.ReadFromJsonAsync<OAIResponse>();
            var raw     = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";
            if (string.IsNullOrEmpty(raw)) return null;

            return ParseCharacterDraft(raw);
        }
        catch
        {
            return null;
        }
    }

    // ── Parsers ────────────────────────────────────────────────────────────────

    private static NarratorResult? ParseResult(string json)
    {
        try
        {
            var stripped = Regex.Replace(json, @"^```(?:json)?\s*|\s*```$", "",
                RegexOptions.Multiline).Trim();

            using var doc  = JsonDocument.Parse(stripped);
            var       root = doc.RootElement;

            string? narration = null;
            if (root.TryGetProperty("narration", out var narEl)
                && narEl.ValueKind == JsonValueKind.String)
            {
                var s = narEl.GetString();
                if (!string.IsNullOrWhiteSpace(s)) narration = s;
            }

            var add = new List<SceneNpc>();
            if (root.TryGetProperty("add", out var addEl)
                && addEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in addEl.EnumerateArray())
                {
                    var name        = item.TryGetProperty("name",        out var n) ? n.GetString() ?? "" : "";
                    var personality = item.TryGetProperty("personality", out var p) ? p.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(name))
                        add.Add(new SceneNpc { Name = name, Personality = personality });
                }
            }

            var remove = new List<string>();
            if (root.TryGetProperty("remove", out var remEl)
                && remEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in remEl.EnumerateArray())
                {
                    var name = item.GetString();
                    if (!string.IsNullOrWhiteSpace(name)) remove.Add(name!);
                }
            }

            int? dc = null;
            if (root.TryGetProperty("dc", out var dcEl) && dcEl.ValueKind == JsonValueKind.Number)
                dc = dcEl.GetInt32();

            string? difficulty = null;
            if (root.TryGetProperty("difficulty", out var diffEl) && diffEl.ValueKind == JsonValueKind.String)
                difficulty = diffEl.GetString();

            if (narration == null && add.Count == 0 && remove.Count == 0 && dc == null)
                return null;

            return new NarratorResult(narration, add, remove, dc, difficulty);
        }
        catch
        {
            return null;
        }
    }

    private static CharacterDraft? ParseCharacterDraft(string raw)
    {
        try
        {
            var stripped = Regex.Replace(raw, @"^```(?:json)?\s*|\s*```$", "",
                RegexOptions.Multiline).Trim();
            using var doc  = JsonDocument.Parse(stripped);
            var       root = doc.RootElement;

            var prompt = root.TryGetProperty("prompt", out var pEl) ? pEl.GetString() ?? "" : raw;
            var stats  = new StatBlock();

            if (root.TryGetProperty("stats", out var sEl))
            {
                if (sEl.TryGetProperty("str", out var v)) stats.Str = v.GetInt32();
                if (sEl.TryGetProperty("dex", out v))     stats.Dex = v.GetInt32();
                if (sEl.TryGetProperty("con", out v))     stats.Con = v.GetInt32();
                if (sEl.TryGetProperty("int", out v))     stats.Int = v.GetInt32();
                if (sEl.TryGetProperty("wis", out v))     stats.Wis = v.GetInt32();
                if (sEl.TryGetProperty("cha", out v))     stats.Cha = v.GetInt32();
                if (sEl.TryGetProperty("hp",  out v))     stats.Hp  = v.GetInt32();
                if (sEl.TryGetProperty("ac",  out v))     stats.Ac  = v.GetInt32();
            }

            return new CharacterDraft(prompt, stats);
        }
        catch
        {
            return new CharacterDraft(raw, new StatBlock());
        }
    }

    private record OAIResponse(
        [property: JsonPropertyName("choices")] List<OAIChoice>? Choices);
    private record OAIChoice(
        [property: JsonPropertyName("message")] OAIMessage? Message);
    private record OAIMessage(
        [property: JsonPropertyName("content")] string? Content);
}
