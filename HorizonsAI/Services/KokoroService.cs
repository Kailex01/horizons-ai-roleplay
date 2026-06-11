using System.IO;
using HorizonsAI;
using HorizonsAI.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;

namespace HorizonsAI.Services;

public sealed class KokoroService : IDisposable
{
    private OfflineTts?   _tts;
    private IReadOnlyDictionary<string, int> _sidMap = SidsV11;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const int SampleRate = 24000;

    // ── Model readiness ────────────────────────────────────────────────────────

    // model_type.txt is written only after full extraction — guards against partial downloads
    public static bool IsModelReady =>
        File.Exists(Path.Combine(AppConfig.TtsFolder, "model_type.txt"));

    // ── Initialization ─────────────────────────────────────────────────────────

    public void Initialize()
    {
        if (_tts != null || !IsModelReady) return;

        var folder     = AppConfig.TtsFolder;
        var markerFile = Path.Combine(folder, "model_type.txt");
        var modelType  = File.Exists(markerFile) ? File.ReadAllText(markerFile).Trim() : "en-v0_19";
        var isMulti    = modelType != "en-v0_19";

        var onnxFile = Directory.GetFiles(folder, "*.onnx").FirstOrDefault()
            ?? throw new FileNotFoundException("No .onnx model file found in tts folder.");

        var config = new OfflineTtsConfig();
        config.Model.Kokoro.Model   = onnxFile;
        config.Model.Kokoro.Voices  = Path.Combine(folder, "voices.bin");
        config.Model.Kokoro.Tokens  = Path.Combine(folder, "tokens.txt");
        config.Model.Kokoro.DataDir = Path.Combine(folder, "espeak-ng-data");
        config.Model.NumThreads     = 2;

        if (isMulti)
        {
            // Multilingual model requires lexicon files and the jieba dict directory
            var lexicons = new[] { "lexicon-us-en.txt", "lexicon-gb-en.txt", "lexicon-zh.txt" }
                .Select(f => Path.Combine(folder, f))
                .Where(File.Exists)
                .ToList();
            if (lexicons.Count > 0)
                config.Model.Kokoro.Lexicon = string.Join(";", lexicons);

            var dictDir = Path.Combine(folder, "dict");
            if (Directory.Exists(dictDir))
                config.Model.Kokoro.DictDir = dictDir;
        }

        _tts    = new OfflineTts(config);
        _sidMap = isMulti ? SidsV11 : SidsV019;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public async Task SpeakAsync(string text, VoiceProfile profile, CancellationToken ct = default)
    {
        if (_tts == null || string.IsNullOrWhiteSpace(text) || !profile.IsEnabled) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var samples = await Task.Run(() => Synthesize(text, profile), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || samples.Length == 0) return;

            samples = ApplyPitch(samples, profile.PitchSemitones);
            ApplyVolume(samples, profile.Volume);

            await PlayAsync(samples, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Synthesis ──────────────────────────────────────────────────────────────

    private float[] Synthesize(string text, VoiceProfile profile)
    {
        var voices = profile.Voices.Where(v => !string.IsNullOrWhiteSpace(v.Voice)).ToList();
        if (voices.Count == 0) return [];

        var totalWeight = voices.Sum(v => v.Weight);
        if (totalWeight <= 0f) totalWeight = 1f;

        var parts = new List<(float[] samples, float weight)>(voices.Count);
        foreach (var entry in voices)
        {
            var sid    = ResolveSid(entry.Voice);
            var result = _tts!.Generate(text, profile.Speed, sid);
            if (result.Samples.Length > 0)
                parts.Add((result.Samples, entry.Weight / totalWeight));
        }

        if (parts.Count == 0) return [];
        return parts.Count == 1 ? parts[0].samples : BlendAudio(parts);
    }

    private static float[] BlendAudio(List<(float[] samples, float weight)> parts)
    {
        var targetLen = parts.Max(p => p.samples.Length);
        var output    = new float[targetLen];

        foreach (var (samples, weight) in parts)
        {
            var aligned = samples.Length == targetLen ? samples : Resample(samples, targetLen);
            for (int i = 0; i < targetLen; i++)
                output[i] += aligned[i] * weight;
        }

        // Prevent clipping from weighted sum
        var peak = output.Max(s => Math.Abs(s));
        if (peak > 0.99f)
        {
            float scale = 0.99f / peak;
            for (int i = 0; i < output.Length; i++) output[i] *= scale;
        }

        return output;
    }

    private static float[] Resample(float[] input, int targetLen)
    {
        if (input.Length == targetLen) return input;
        if (targetLen <= 1) return [input[0]];

        var    output = new float[targetLen];
        double step   = (double)(input.Length - 1) / (targetLen - 1);

        for (int i = 0; i < targetLen; i++)
        {
            double pos  = i * step;
            int    lo   = (int)pos;
            int    hi   = Math.Min(lo + 1, input.Length - 1);
            float  t    = (float)(pos - lo);
            output[i]   = input[lo] * (1f - t) + input[hi] * t;
        }
        return output;
    }

    // ── Post-processing ────────────────────────────────────────────────────────

    private static float[] ApplyPitch(float[] samples, float semitones)
    {
        if (Math.Abs(semitones) < 0.01f) return samples;

        float factor = MathF.Pow(2f, semitones / 12f);
        var   format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
        var   bytes  = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        var ms      = new MemoryStream(bytes);
        var raw     = new RawSourceWaveStream(ms, format);
        var pitched = new SmbPitchShiftingSampleProvider(raw.ToSampleProvider());
        pitched.PitchFactor = factor;

        var result = new List<float>(samples.Length);
        var buf    = new float[4096];
        int read;
        while ((read = pitched.Read(buf, 0, buf.Length)) > 0)
            result.AddRange(new ArraySegment<float>(buf, 0, read));

        return result.ToArray();
    }

    private static void ApplyVolume(float[] samples, float volume)
    {
        if (Math.Abs(volume - 1f) < 0.01f) return;
        for (int i = 0; i < samples.Length; i++)
            samples[i] = Math.Clamp(samples[i] * volume, -1f, 1f);
    }

    // ── Playback ───────────────────────────────────────────────────────────────

    private static async Task PlayAsync(float[] samples, CancellationToken ct)
    {
        if (samples.Length == 0 || ct.IsCancellationRequested) return;

        var format   = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
        var bytes    = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        var durationSec = (double)samples.Length / SampleRate;
        var provider    = new BufferedWaveProvider(format) { DiscardOnBufferOverflow = false };
        provider.BufferDuration = TimeSpan.FromSeconds(durationSec + 1.0);
        provider.AddSamples(bytes, 0, bytes.Length);

        using var waveOut = new WaveOutEvent();
        waveOut.Init(provider);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waveOut.PlaybackStopped += (_, _) => tcs.TrySetResult();

        using var reg = ct.Register(() => { waveOut.Stop(); tcs.TrySetResult(); });

        waveOut.Play();
        await tcs.Task.ConfigureAwait(false);
    }

    // ── Voice SID tables ───────────────────────────────────────────────────────

    private int ResolveSid(string voiceName)
    {
        var key = voiceName.Trim();
        return _sidMap.TryGetValue(key, out var sid) ? sid : 0;
    }

    // kokoro-multi-lang-v1_1: alphabetically-ordered within each prefix group
    private static readonly IReadOnlyDictionary<string, int> SidsV11 =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // American Female (0–10)
            ["af_alloy"]     = 0,  ["af_aoede"]       = 1,  ["af_bella"]   = 2,
            ["af_heart"]     = 3,  ["af_jessica"]     = 4,  ["af_kore"]    = 5,
            ["af_nicole"]    = 6,  ["af_nova"]        = 7,  ["af_river"]   = 8,
            ["af_sarah"]     = 9,  ["af_sky"]         = 10,
            // American Male (11–19)
            ["am_adam"]      = 11, ["am_echo"]        = 12, ["am_eric"]    = 13,
            ["am_fenrir"]    = 14, ["am_liam"]        = 15, ["am_michael"] = 16,
            ["am_onyx"]      = 17, ["am_puck"]        = 18, ["am_santa"]   = 19,
            // British Female (20–23)
            ["bf_alice"]     = 20, ["bf_emma"]        = 21, ["bf_isabella"]= 22,
            ["bf_lily"]      = 23,
            // British Male (24–27)
            ["bm_daniel"]    = 24, ["bm_fable"]       = 25, ["bm_george"]  = 26,
            ["bm_lewis"]     = 27,
            // Japanese Female (37–40)
            ["jf_alpha"]     = 37, ["jf_gongitsune"]  = 38, ["jf_nezumi"]  = 39,
            ["jf_tebukuro"]  = 40,
            // Japanese Male (41)
            ["jm_kumo"]      = 41,
        };

    // kokoro-en-v0_19: 11 English speakers
    private static readonly IReadOnlyDictionary<string, int> SidsV019 =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["af"]           = 0,  ["af_bella"]   = 1,  ["af_nicole"]   = 2,
            ["af_sarah"]     = 3,  ["af_sky"]     = 4,  ["am_adam"]     = 5,
            ["am_michael"]   = 6,  ["bf_emma"]    = 7,  ["bf_isabella"] = 8,
            ["bm_george"]    = 9,  ["bm_lewis"]   = 10,
        };

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _tts?.Dispose();
        _tts = null;
        _lock.Dispose();
    }
}
