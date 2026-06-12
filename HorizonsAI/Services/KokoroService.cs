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
    private IReadOnlyDictionary<string, int> _sidMap = SidsV10;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const int SampleRate = 24000;

    // ── Model readiness ────────────────────────────────────────────────────────

    // model_type.txt is written only after full extraction — guards against partial downloads
    public static bool IsModelReady =>
        File.Exists(Path.Combine(AppConfig.TtsFolder, "model_type.txt"));

    public bool IsInitialized => _tts != null;

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
                config.Model.Kokoro.Lexicon = string.Join(",", lexicons);

            var dictDir = Path.Combine(folder, "dict");
            if (Directory.Exists(dictDir))
                config.Model.Kokoro.DictDir = dictDir;
        }

        _tts    = new OfflineTts(config);
        _sidMap = modelType switch
        {
            "multi-v1_1" => SidsV11,
            "multi-v1_0" => SidsV10,
            _            => SidsV019,
        };
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public async Task SpeakAsync(string text, VoiceProfile profile, CancellationToken ct = default)
    {
        if (_tts == null || string.IsNullOrWhiteSpace(text) || !profile.IsEnabled) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (samples, sampleRate) = await Task.Run(() => Synthesize(text, profile), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || samples.Length == 0) return;

            samples = ApplyPitch(samples, profile.PitchSemitones, sampleRate);
            ApplyVolume(samples, profile.Volume);

            await PlayAsync(samples, sampleRate, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Synthesis ──────────────────────────────────────────────────────────────

    private (float[] Samples, int SampleRate) Synthesize(string text, VoiceProfile profile)
    {
        var voices = profile.Voices.Where(v => !string.IsNullOrWhiteSpace(v.Voice)).ToList();
        if (voices.Count == 0) return ([], SampleRate);

        var totalWeight = voices.Sum(v => v.Weight);
        if (totalWeight <= 0f) totalWeight = 1f;

        var parts      = new List<(float[] samples, float weight)>(voices.Count);
        var sampleRate = SampleRate; // updated from first successful result
        foreach (var entry in voices)
        {
            var sid = ResolveSid(entry.Voice);
            try
            {
                var result  = _tts!.Generate(text, profile.Speed, sid);
                var samples = result?.Samples;
                if (samples?.Length > 0)
                {
                    sampleRate = result!.SampleRate > 0 ? result.SampleRate : SampleRate;
                    parts.Add((samples, entry.Weight / totalWeight));
                }
            }
            catch { /* native generation failed for this voice — skip it */ }
        }

        if (parts.Count == 0) return ([], sampleRate);
        var mixed = parts.Count == 1 ? parts[0].samples : BlendAudio(parts);
        return (mixed, sampleRate);
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

    private static float[] ApplyPitch(float[] samples, float semitones, int sampleRate)
    {
        if (Math.Abs(semitones) < 0.01f) return samples;

        float factor = MathF.Pow(2f, semitones / 12f);
        var   format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
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

    private static async Task PlayAsync(float[] samples, int sampleRate, CancellationToken ct)
    {
        if (samples.Length == 0 || ct.IsCancellationRequested) return;

        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        var bytes  = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        // Duration-based fallback: WaveOutEvent doesn't always fire PlaybackStopped
        // when the stream runs dry, so we stop after the calculated duration + 400ms
        // flush buffer. If PlaybackStopped fires first (ideal path), we return early.
        var durationMs = (int)((double)samples.Length / sampleRate * 1000) + 400;

        using var ms           = new MemoryStream(bytes);
        using var stream       = new RawSourceWaveStream(ms, format);
        using var waveOut      = new WaveOutEvent();
        using var durationCts  = new CancellationTokenSource(durationMs);
        using var linked       = CancellationTokenSource.CreateLinkedTokenSource(ct, durationCts.Token);
        waveOut.Init(stream);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waveOut.PlaybackStopped += (_, _) => tcs.TrySetResult();
        using var reg = linked.Token.Register(() => { waveOut.Stop(); tcs.TrySetResult(); });

        waveOut.Play();
        await tcs.Task.ConfigureAwait(false);
    }

    // ── Voice SID tables ───────────────────────────────────────────────────────

    private int ResolveSid(string voiceName)
    {
        var key = voiceName.Trim();
        return _sidMap.TryGetValue(key, out var sid) ? sid : 0;
    }

    // kokoro-multi-lang-v1_0: 53 voices — confirmed ordering from sherpa-onnx source
    private static readonly IReadOnlyDictionary<string, int> SidsV10 =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // American Female (0–10)
            ["af_alloy"]     = 0,  ["af_aoede"]      = 1,  ["af_bella"]    = 2,
            ["af_heart"]     = 3,  ["af_jessica"]    = 4,  ["af_kore"]     = 5,
            ["af_nicole"]    = 6,  ["af_nova"]       = 7,  ["af_river"]    = 8,
            ["af_sarah"]     = 9,  ["af_sky"]        = 10,
            // American Male (11–19)
            ["am_adam"]      = 11, ["am_echo"]       = 12, ["am_eric"]     = 13,
            ["am_fenrir"]    = 14, ["am_liam"]       = 15, ["am_michael"]  = 16,
            ["am_onyx"]      = 17, ["am_puck"]       = 18, ["am_santa"]    = 19,
            // British Female (20–23)
            ["bf_alice"]     = 20, ["bf_emma"]       = 21, ["bf_isabella"] = 22,
            ["bf_lily"]      = 23,
            // British Male (24–27)
            ["bm_daniel"]    = 24, ["bm_fable"]      = 25, ["bm_george"]   = 26,
            ["bm_lewis"]     = 27,
            // Spanish Female/Male (28–29)
            ["ef_dora"]      = 28, ["em_alex"]       = 29,
            // French Female (30)
            ["ff_siwis"]     = 30,
            // Hindi Female/Male (31–34)
            ["hf_alpha"]     = 31, ["hf_beta"]       = 32,
            ["hm_omega"]     = 33, ["hm_psi"]        = 34,
            // Italian Female/Male (35–36)
            ["if_sara"]      = 35, ["im_nicola"]     = 36,
            // Japanese Female (37–40)
            ["jf_alpha"]     = 37, ["jf_gongitsune"] = 38, ["jf_nezumi"]   = 39,
            ["jf_tebukuro"]  = 40,
            // Japanese Male (41)
            ["jm_kumo"]      = 41,
            // Portuguese Female/Male (42–44)
            ["pf_dora"]      = 42, ["pm_alex"]       = 43, ["pm_santa"]    = 44,
            // Chinese Female (45–48)
            ["zf_xiaobei"]   = 45, ["zf_xiaoni"]     = 46, ["zf_xiaoxiao"] = 47,
            ["zf_xiaoyi"]    = 48,
            // Chinese Male (49–52)
            ["zm_yunjian"]   = 49, ["zm_yunxi"]      = 50, ["zm_yunxia"]   = 51,
            ["zm_yunyang"]   = 52,
        };

    // kokoro-multi-lang-v1_1: 103 voices — 3 EN + 55 ZH-female + 45 ZH-male
    private static readonly IReadOnlyDictionary<string, int> SidsV11 = BuildSidsV11();
    private static IReadOnlyDictionary<string, int> BuildSidsV11()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["af_maple"] = 0,
            ["af_sol"]   = 1,
            ["bf_vale"]  = 2,
        };
        for (int i = 1;  i <= 55; i++) d[$"zf_{i:000}"] = i + 2;   // zf_001=3 … zf_055=57
        for (int i = 9;  i <= 53; i++) d[$"zm_{i:000}"] = i + 49;  // zm_009=58 … zm_053=102
        return d;
    }

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
