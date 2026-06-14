using System.Collections.Concurrent;
using System.Threading.Channels;
using GukVoice.Models;

namespace GukVoice.Services;

public sealed class TtsQueueService : IDisposable
{
    private readonly KokoroService _kokoro;
    private readonly Channel<(SpeakerProfile Speaker, string Text)> _channel;
    private readonly ConcurrentDictionary<string, int> _pendingCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();

    // Fires on the background thread — callers must dispatch to UI if needed
    public event Action<string?>?         SpeakingChanged;  // speaker name, or null when idle
    public event Action<string, int>?     PendingChanged;   // speaker name, new count

    public TtsQueueService(KokoroService kokoro)
    {
        _kokoro  = kokoro;
        _channel = Channel.CreateBounded<(SpeakerProfile, string)>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest });
        _ = ProcessAsync(_cts.Token);
    }

    public void Enqueue(SpeakerProfile speaker, string text)
    {
        if (!speaker.Enabled || !speaker.VoiceProfile.IsEnabled) return;
        var count = _pendingCounts.AddOrUpdate(speaker.Name, 1, (_, c) => c + 1);
        PendingChanged?.Invoke(speaker.Name, count);
        _channel.Writer.TryWrite((speaker, text));
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        await foreach (var (speaker, text) in _channel.Reader.ReadAllAsync(ct))
        {
            // Decrement pending before speaking starts
            var remaining = _pendingCounts.AddOrUpdate(speaker.Name, 0, (_, c) => Math.Max(0, c - 1));
            PendingChanged?.Invoke(speaker.Name, remaining);

            SpeakingChanged?.Invoke(speaker.Name);
            try
            {
                await _kokoro.SpeakAsync(text, speaker.VoiceProfile,
                    AppConfig.Current.NarratorVoice, ct: ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch { }
            SpeakingChanged?.Invoke(null);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        _cts.Dispose();
    }
}
