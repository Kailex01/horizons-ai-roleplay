using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HorizonsAI.Models;

namespace HorizonsAI.Services;

public class OpenRouterService
{
    private readonly HttpClient _http;
    private const string BaseUrl = "https://openrouter.ai/api/v1";

    public OpenRouterService(HttpClient http) => _http = http;

    // ── Single character chat ──────────────────────────────────────────────────

    public async Task<List<string>> ChatAsync(Character character, IEnumerable<ChatMessage> history, string userMessage, Character? playAs = null, string? memory = null)
    {
        var apiKey = AppConfig.Current.OpenRouterApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return ["(No OpenRouter API key set — open Settings to add one.)"];

        var model    = string.IsNullOrWhiteSpace(character.Model) ? AppConfig.Current.DefaultModel : character.Model;
        var messages = BuildSingleMessages(character, history, userMessage, playAs, memory);
        var text     = await SendAsync(model, messages);

        if (string.IsNullOrEmpty(text)) return ["…"];

        return text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .Where(s => !string.IsNullOrEmpty(s))
                   .ToList();
    }

    // ── Party chat ─────────────────────────────────────────────────────────────

    public async Task<List<(string Name, string Text)>> ChatPartyAsync(
        Party party,
        IEnumerable<Character> members,
        IEnumerable<ChatMessage> history,
        string userMessage,
        Character? playAs = null,
        string? memory = null)
    {
        var apiKey = AppConfig.Current.OpenRouterApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return [("", "(No OpenRouter API key set — open Settings to add one.)")];

        var memberList = members.ToList();
        var model      = AppConfig.Current.DefaultModel;
        var messages   = BuildPartyMessages(party, memberList, history, userMessage, playAs, memory);
        var text       = await SendAsync(model, messages);

        if (string.IsNullOrEmpty(text)) return [("", "…")];

        var parsed = ParsePartyResponse(text);
        return parsed.Count > 0 ? parsed : [("", text.Trim())];
    }

    public static List<(string Name, string Text)> ParsePartyResponse(string response)
    {
        var result  = new List<(string, string)>();
        var pattern = new Regex(@"\*\*(.+?)\*\*:\s*");
        var parts   = pattern.Split(response);

        // Split returns: [pre-text, name1, text1, name2, text2, ...]
        for (int i = 1; i + 1 < parts.Length; i += 2)
        {
            var name = parts[i].Trim();
            var text = parts[i + 1].Trim();
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(text))
                result.Add((name, text));
        }
        return result;
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private async Task<string> SendAsync(string model, List<object> messages)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AppConfig.Current.OpenRouterApiKey);
        request.Headers.Add("HTTP-Referer", "https://github.com/Kailex01/horizons-ai-roleplay");
        request.Headers.Add("X-Title", "Horizon's AI");
        request.Content = JsonContent.Create(new { model, messages });

        var resp   = await _http.SendAsync(request);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<OAIResponse>();
        return result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";
    }

    public async Task<string> SummarizeAsync(IEnumerable<ChatMessage> messages, string? existingMemory)
    {
        var apiKey = AppConfig.Current.OpenRouterApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return "";

        var lines = string.Join("\n", messages
            .Where(m => !m.IsSummary)
            .Select(m => $"{m.SenderName}: {m.Text}"));

        var content = string.IsNullOrEmpty(existingMemory)
            ? lines
            : $"Previous summary:\n{existingMemory}\n\nNew messages:\n{lines}";

        var apiMessages = new List<object>
        {
            new { role = "system", content = "Summarize this roleplay conversation into 2-3 sentences, capturing key events, relationships, and important context established. Be concise." },
            new { role = "user",   content }
        };

        return await SendAsync(AppConfig.Current.DefaultModel, apiMessages);
    }

    private static List<object> BuildSingleMessages(Character character, IEnumerable<ChatMessage> history, string userMessage, Character? playAs, string? memory)
    {
        var system = character.SystemPrompt ?? "";
        if (!string.IsNullOrEmpty(memory))
            system += $"\n\n---\nContext from earlier in this conversation:\n{memory}";
        if (playAs != null)
        {
            system += $"\n\n---\nYou are speaking with {playAs.Name}.";
            if (!string.IsNullOrWhiteSpace(playAs.SystemPrompt))
                system += $"\n{playAs.SystemPrompt}";
        }

        var msgs = new List<object>();
        if (!string.IsNullOrWhiteSpace(system))
            msgs.Add(new { role = "system", content = system });

        foreach (var msg in history.Where(m => !m.IsSummary))
        {
            var content = msg.IsPlayer && !string.IsNullOrEmpty(msg.SenderName)
                ? $"{msg.SenderName}: {msg.Text}"
                : msg.Text;
            msgs.Add(new { role = msg.IsPlayer ? "user" : "assistant", content });
        }

        var current = playAs != null ? $"{playAs.Name}: {userMessage}" : userMessage;
        msgs.Add(new { role = "user", content = current });
        return msgs;
    }

    private static List<object> BuildPartyMessages(Party party, List<Character> members, IEnumerable<ChatMessage> history, string userMessage, Character? playAs, string? memory)
    {
        var profiles = string.Join("\n\n", members.Select(m =>
            $"## {m.Name}\n{m.SystemPrompt}"));

        var memorySec = string.IsNullOrEmpty(memory) ? "" :
            $"\n\nContext from earlier in this conversation:\n{memory}";

        var playerSection = "";
        if (playAs != null)
        {
            playerSection = $"\n\nThe player is speaking as {playAs.Name}.";
            if (!string.IsNullOrWhiteSpace(playAs.SystemPrompt))
                playerSection += $"\n{playAs.SystemPrompt}";
        }

        var system = $"""
            You are managing a collaborative roleplay with multiple characters.

            {profiles}

            {(string.IsNullOrWhiteSpace(party.Context) ? "" : $"Scene context: {party.Context}")}
            {memorySec}
            {playerSection}

            When the player speaks, respond as whichever characters would naturally react.
            Not every character needs to respond — only those with something relevant to say.
            Format each response exactly as:
            **CharacterName:** [their response]

            Maintain each character's distinct personality and voice.
            """;

        var msgs = new List<object> { new { role = "system", content = system } };

        foreach (var msg in history.Where(m => !m.IsSummary))
        {
            var content = msg.IsPlayer && !string.IsNullOrEmpty(msg.SenderName)
                ? $"{msg.SenderName}: {msg.Text}"
                : $"**{msg.SenderName}:** {msg.Text}";
            msgs.Add(new { role = msg.IsPlayer ? "user" : "assistant", content });
        }

        var currentMsg = playAs != null ? $"{playAs.Name}: {userMessage}" : userMessage;
        msgs.Add(new { role = "user", content = currentMsg });
        return msgs;
    }

    private record OAIResponse(
        [property: JsonPropertyName("choices")] List<OAIChoice>? Choices);
    private record OAIChoice(
        [property: JsonPropertyName("message")] OAIMessage? Message);
    private record OAIMessage(
        [property: JsonPropertyName("content")] string? Content);
}
