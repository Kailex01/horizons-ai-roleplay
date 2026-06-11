using System.Diagnostics;
using System.Media;
using System.Text.RegularExpressions;

namespace HorizonsAI.Services;

public static class PiperService
{
    // Speaks a full line, handling *emote* segments as narrator reads.
    // e.g. "*snorts* You dare challenge me?" becomes:
    //   → narrator: "CharacterName snorts"
    //   → character: "You dare challenge me?"
    public static async Task SpeakLineAsync(string line, string characterName, string? voiceModel)
    {
        var narratorModel = AppConfig.Current.NarratorVoiceModel;
        foreach (var (text, isEmote) in ParseSegments(line))
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (isEmote)
                await SpeakAsync($"{characterName} {text}",
                    string.IsNullOrWhiteSpace(narratorModel) ? voiceModel : narratorModel);
            else
                await SpeakAsync(text, voiceModel);
        }
    }

    // Speaks multiple lines in order, each with emote handling.
    public static async Task SpeakLinesAsync(IEnumerable<string> lines, string characterName, string? voiceModel)
    {
        foreach (var line in lines)
            await SpeakLineAsync(line, characterName, voiceModel);
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private static async Task SpeakAsync(string text, string? voiceModel)
    {
        var exePath    = AppConfig.Current.PiperExePath;
        var modelsPath = AppConfig.Current.PiperModelsPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return;

        var model = ResolveModel(voiceModel, modelsPath);
        if (model is null) return;

        var wavPath = Path.Combine(Path.GetTempPath(), $"gukchat_{Guid.NewGuid():N}.wav");
        try
        {
            await RunPiperAsync(exePath, model, text, wavPath);
            if (File.Exists(wavPath))
                await PlayAndDeleteAsync(wavPath);
        }
        catch
        {
            TryDelete(wavPath);
        }
    }

    // Splits a line into (text, isEmote) segments.
    // "*snorts* Hello there" → [("snorts", true), ("Hello there", false)]
    private static IEnumerable<(string Text, bool IsEmote)> ParseSegments(string line)
    {
        var segments = new List<(string, bool)>();
        var regex    = new Regex(@"\*([^*]+)\*");
        int pos      = 0;

        foreach (Match match in regex.Matches(line))
        {
            if (match.Index > pos)
            {
                var before = line[pos..match.Index].Trim();
                if (!string.IsNullOrEmpty(before))
                    segments.Add((before, false));
            }
            segments.Add((match.Groups[1].Value.Trim(), true));
            pos = match.Index + match.Length;
        }

        if (pos < line.Length)
        {
            var remaining = line[pos..].Trim();
            if (!string.IsNullOrEmpty(remaining))
                segments.Add((remaining, false));
        }

        return segments;
    }

    private static string? ResolveModel(string? voiceModel, string modelsPath)
    {
        if (string.IsNullOrWhiteSpace(modelsPath)) return null;

        if (!string.IsNullOrWhiteSpace(voiceModel))
        {
            var named = Path.Combine(modelsPath,
                voiceModel.EndsWith(".onnx") ? voiceModel : voiceModel + ".onnx");
            if (File.Exists(named)) return named;
        }

        return Directory.EnumerateFiles(modelsPath, "*.onnx").FirstOrDefault();
    }

    private static async Task RunPiperAsync(string exePath, string model, string text, string wavPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName              = exePath,
            Arguments             = $"--model \"{model}\" --output_file \"{wavPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var proc = Process.Start(psi)!;
        await proc.StandardInput.WriteLineAsync(text);
        proc.StandardInput.Close();
        await proc.WaitForExitAsync();
    }

    // Plays synchronously on a thread pool thread so it blocks that thread
    // (not the UI) until playback finishes — this is what keeps lines in order.
    private static Task PlayAndDeleteAsync(string wavPath) => Task.Run(() =>
    {
        try
        {
            using var player = new SoundPlayer(wavPath);
            player.PlaySync();
        }
        finally { TryDelete(wavPath); }
    });

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
