using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HorizonsAI.Models;

namespace HorizonsAI.Services;


public record TokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens);

public class OpenRouterService
{
    private readonly HttpClient _http;
    private const string BaseUrl = "https://openrouter.ai/api/v1";

    public TokenUsage? LastUsage { get; private set; }

    public OpenRouterService(HttpClient http) => _http = http;

    // ── Single character chat ──────────────────────────────────────────────────

    // ── Lore matching ──────────────────────────────────────────────────────────

    public static List<LoreEntry> MatchLore(IEnumerable<ChatMessage> recentMessages, string userMessage, IEnumerable<LoreEntry> entries)
    {
        var combined = string.Join(" ", recentMessages.TakeLast(10).Select(m => m.Text).Append(userMessage))
                             .ToLowerInvariant();
        return entries
            .Where(e => e.Enabled && e.Keywords.Any(k => combined.Contains(k.Trim().ToLowerInvariant())))
            .ToList();
    }

    // ── Single character chat ──────────────────────────────────────────────────

    public async Task<List<string>> ChatAsync(Character character, IEnumerable<ChatMessage> history, string userMessage, Character? playAs = null, string? memory = null, IReadOnlyList<LoreEntry>? lore = null, string? authorsNote = null)
    {
        var apiKey = AppConfig.Current.OpenRouterApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return ["(No OpenRouter API key set — open Settings to add one.)"];

        var model    = string.IsNullOrWhiteSpace(character.Model) ? AppConfig.Current.DefaultModel : character.Model;
        var messages = BuildSingleMessages(character, history, userMessage, playAs, memory, lore, authorsNote);
        var text     = await SendAsync(model, messages, AppConfig.Current.MaxReplyTokens);

        if (string.IsNullOrEmpty(text)) return ["…"];
        return [text.Trim()];
    }

    // ── Party chat ─────────────────────────────────────────────────────────────

    public async Task<List<(string Name, string Text)>> ChatPartyAsync(
        string partyContext,
        IEnumerable<(string Name, string SystemPrompt)> members,
        IEnumerable<ChatMessage> history,
        string userMessage,
        Character? playAs = null,
        string? memory = null,
        IReadOnlyList<LoreEntry>? lore = null,
        string? authorsNote = null)
    {
        var apiKey = AppConfig.Current.OpenRouterApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return [("", "(No OpenRouter API key set — open Settings to add one.)")];

        var memberList  = members.ToList();
        var model       = AppConfig.Current.DefaultModel;
        var messages    = BuildPartyMessages(partyContext, memberList, history, userMessage, playAs, memory, lore, authorsNote);
        var tokenBudget = AppConfig.Current.MaxReplyTokens * Math.Max(1, memberList.Count);
        var text        = await SendAsync(model, messages, tokenBudget);

        if (string.IsNullOrEmpty(text)) return [("", "…")];

        var parsed = ParsePartyResponse(text);
        return parsed.Count > 0 ? parsed : [("", text.Trim())];
    }

    public static List<(string Name, string Text)> ParsePartyResponse(string response)
    {
        var result  = new List<(string, string)>();
        var pattern = new Regex(@"\*\*([^*]+?):?\*\*:?\s*");
        var parts   = pattern.Split(response);

        // Split returns: [pre-text, name1, text1, name2, text2, ...]
        for (int i = 1; i + 1 < parts.Length; i += 2)
        {
            var name = parts[i].Trim().TrimEnd(':');
            var text = parts[i + 1].Trim();
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(text))
                result.Add((name, text));
        }
        return result;
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private async Task<string> SendAsync(string model, List<object> messages, int maxTokens = 400)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AppConfig.Current.OpenRouterApiKey);
        request.Headers.Add("HTTP-Referer", "https://github.com/Kailex01/horizons-ai-roleplay");
        request.Headers.Add("X-Title", "Horizon's AI");
        request.Content = JsonContent.Create(new { model, messages, max_tokens = maxTokens });

        var resp   = await _http.SendAsync(request);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<OAIResponse>();
        LastUsage = result?.Usage is { } u
            ? new TokenUsage(u.PromptTokens, u.CompletionTokens, u.TotalTokens)
            : null;
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

        return await SendAsync(AppConfig.Current.DefaultModel, apiMessages, maxTokens: 600);
    }

    private static List<object> BuildSingleMessages(Character character, IEnumerable<ChatMessage> history, string userMessage, Character? playAs, string? memory, IReadOnlyList<LoreEntry>? lore, string? authorsNote)
    {
        var defaultPrompt = AppConfig.Current.DefaultCharacterPrompt;
        var system = string.IsNullOrWhiteSpace(defaultPrompt)
            ? (character.SystemPrompt ?? "")
            : $"{defaultPrompt}\n\n---\n{character.SystemPrompt ?? ""}";
        if (lore?.Count > 0)
            system += "\n\n---\nWorld Knowledge:\n" + string.Join("\n\n", lore.Select(e => $"**{e.Title}**\n{e.Content}"));
        if (!string.IsNullOrEmpty(memory))
            system += $"\n\n---\nContext from earlier in this conversation:\n{memory}";
        if (playAs != null)
        {
            system += $"\n\n---\nYou are speaking with {playAs.Name}.";
            if (!string.IsNullOrWhiteSpace(playAs.SystemPrompt))
                system += $"\n{playAs.SystemPrompt}";
        }
        system += $"\n\n---\nYou are {character.Name} and you speak ONLY as {character.Name}. " +
                  "Never write dialogue or actions for any other character — not even unnamed ones. " +
                  "Do not narrate the scene or introduce plot events. " +
                  "The Game Master controls the world; you only react to it.\n\n" +
                  "GAME MECHANICS: When your character attempts a skill check or attack, embed a token in your response. " +
                  "The engine will resolve it and replace it with the result automatically.\n" +
                  "  [check str]  — a strength-based skill check (any stat: str/dex/con/int/wis/cha)\n" +
                  "  [attack str]  — a weapon or tool attack (1d6 damage)\n" +
                  "  [attack str simple]  — unarmed or improvised attack (1d4 damage)\n" +
                  "  Add an optional modifier: [attack str +2] or [check dex -1]\n" +
                  "Player action results already in the history appear as [success], [fail], [hit: N dmg], or [miss].";

        var msgs = new List<object>();
        if (!string.IsNullOrWhiteSpace(system))
            msgs.Add(new { role = "system", content = system });

        foreach (var msg in history.Where(m => !m.IsSummary))
        {
            if (msg.IsNarratorAction)
            {
                msgs.Add(new { role = "system", content = $"[Scene: {msg.Text}]" });
                continue;
            }
            var text = msg.IsPlayer ? SimplifyTokens(msg.Text) : msg.Text;
            var content = msg.IsPlayer && !string.IsNullOrEmpty(msg.SenderName)
                ? $"{msg.SenderName}: {text}"
                : text;
            msgs.Add(new { role = msg.IsPlayer ? "user" : "assistant", content });
        }

        if (!string.IsNullOrEmpty(authorsNote))
            msgs.Add(new { role = "system", content = $"[Author's note: {authorsNote}]" });
        var current = playAs != null ? $"{playAs.Name}: {SimplifyTokens(userMessage)}" : SimplifyTokens(userMessage);
        msgs.Add(new { role = "user", content = current });
        return msgs;
    }

    private static List<object> BuildPartyMessages(string partyContext, List<(string Name, string SystemPrompt)> members, IEnumerable<ChatMessage> history, string userMessage, Character? playAs, string? memory, IReadOnlyList<LoreEntry>? lore, string? authorsNote)
    {
        var defaultPrompt = AppConfig.Current.DefaultCharacterPrompt;
        var profiles = string.Join("\n\n", members.Select(m =>
            string.IsNullOrWhiteSpace(defaultPrompt)
                ? $"## {m.Name}\n{m.SystemPrompt}"
                : $"## {m.Name}\n{defaultPrompt}\n\n---\n{m.SystemPrompt}"));

        var loreSec   = (lore?.Count > 0)
            ? "\n\nWorld Knowledge:\n" + string.Join("\n\n", lore.Select(e => $"**{e.Title}**\n{e.Content}"))
            : "";
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

            {(string.IsNullOrWhiteSpace(partyContext) ? "" : $"Scene context: {partyContext}")}
            {loreSec}
            {memorySec}
            {playerSection}

            When the player speaks, respond as whichever characters would naturally react.
            Not every character needs to respond — only those with something relevant to say.
            Format each response exactly as:
            **CharacterName:** [their response]

            Maintain each character's distinct personality and voice.
            Each character must stay in their own role and react naturally as themselves.
            Characters must NOT narrate the scene, introduce plot events, or direct the story
            — the Game Master controls the world.

            GAME MECHANICS: Any character may embed a token to trigger a dice roll:
              [check str] — skill check (any stat: str/dex/con/int/wis/cha)
              [attack str] — weapon/tool attack (1d6 damage)
              [attack str simple] — unarmed/improvised attack (1d4 damage)
              Optional modifier: [attack str +2] or [check dex -1]
            The engine resolves the roll and replaces the token with the result.
            Player action results in the history appear as [success], [fail], [hit: N dmg], or [miss].
            """;

        var msgs = new List<object> { new { role = "system", content = system } };

        foreach (var msg in history.Where(m => !m.IsSummary))
        {
            if (msg.IsNarratorAction)
            {
                msgs.Add(new { role = "system", content = $"[Scene: {msg.Text}]" });
                continue;
            }
            var text = msg.IsPlayer ? SimplifyTokens(msg.Text) : msg.Text;
            var content = msg.IsPlayer && !string.IsNullOrEmpty(msg.SenderName)
                ? $"{msg.SenderName}: {text}"
                : $"**{msg.SenderName}:** {text}";
            msgs.Add(new { role = msg.IsPlayer ? "user" : "assistant", content });
        }

        if (!string.IsNullOrEmpty(authorsNote))
            msgs.Add(new { role = "system", content = $"[Author's note: {authorsNote}]" });
        var currentMsg = playAs != null ? $"{playAs.Name}: {SimplifyTokens(userMessage)}" : SimplifyTokens(userMessage);
        msgs.Add(new { role = "user", content = currentMsg });
        return msgs;
    }

    // Replaces resolved game tokens with compact labels for NPC consumption
    internal static string SimplifyTokens(string text)
    {
        text = Regex.Replace(text, @"\[[^\]]*?Attack[^\]]*?— HIT! 1d\d+=(\d+) dmg\]", "[hit: $1 dmg]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[[^\]]*?Attack[^\]]*?— MISS\]",                  "[miss]",         RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[[^\]]*?Check[^\]]*?— SUCCESS\]",                "[success]",      RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[[^\]]*?Check[^\]]*?— FAIL\]",                   "[fail]",         RegexOptions.IgnoreCase);
        return text;
    }

    private record OAIResponse(
        [property: JsonPropertyName("choices")] List<OAIChoice>? Choices,
        [property: JsonPropertyName("usage")]   OAIUsage?        Usage);
    private record OAIChoice(
        [property: JsonPropertyName("message")] OAIMessage? Message);
    private record OAIMessage(
        [property: JsonPropertyName("content")] string? Content);
    private record OAIUsage(
        [property: JsonPropertyName("prompt_tokens")]     int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
        [property: JsonPropertyName("total_tokens")]      int TotalTokens);
}
