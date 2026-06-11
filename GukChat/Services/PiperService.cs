using System.Diagnostics;
using System.Media;

namespace GukChat.Services;

public static class PiperService
{
    public static async Task SpeakAsync(string text, string? voiceModel = null)
    {
        var exePath    = AppConfig.Current.PiperExePath;
        var modelsPath = AppConfig.Current.PiperModelsPath;

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        var model = ResolveModel(voiceModel, modelsPath);
        if (model is null) return;

        var wavPath = Path.Combine(Path.GetTempPath(), $"gukchat_{Guid.NewGuid():N}.wav");
        try
        {
            await RunPiperAsync(exePath, model, text, wavPath);
            if (File.Exists(wavPath))
                PlayAndDelete(wavPath);
        }
        catch
        {
            TryDelete(wavPath);
        }
    }

    private static string? ResolveModel(string? voiceModel, string modelsPath)
    {
        if (string.IsNullOrWhiteSpace(modelsPath)) return null;

        // If a specific model name is given, look for it in the models folder
        if (!string.IsNullOrWhiteSpace(voiceModel))
        {
            var named = Path.Combine(modelsPath, voiceModel.EndsWith(".onnx") ? voiceModel : voiceModel + ".onnx");
            if (File.Exists(named)) return named;
        }

        // Fall back to the first .onnx file found in the models folder
        return Directory.EnumerateFiles(modelsPath, "*.onnx")
                        .FirstOrDefault();
    }

    private static async Task RunPiperAsync(string exePath, string model, string text, string wavPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = exePath,
            Arguments              = $"--model \"{model}\" --output_file \"{wavPath}\"",
            RedirectStandardInput  = true,
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

    private static void PlayAndDelete(string wavPath)
    {
        // SoundPlayer plays synchronously on a threadpool thread so it doesn't block the UI
        Task.Run(() =>
        {
            try
            {
                using var player = new SoundPlayer(wavPath);
                player.PlaySync();
            }
            finally { TryDelete(wavPath); }
        });
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
