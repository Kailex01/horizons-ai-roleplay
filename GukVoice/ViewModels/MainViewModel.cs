using GukVoice.Models;
using GukVoice.Services;

namespace GukVoice.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly KokoroService    _kokoro;
    private readonly TtsQueueService  _ttsQueue;
    private readonly EqLogWatcher     _watcher;
    private readonly EqProcessMonitor _processMonitor;

    public ObservableCollection<SpeakerItem>  Speakers     { get; } = new();
    public ObservableCollection<ActivityItem> ActivityFeed { get; } = new();
    public CombatViewModel                    Combat       { get; } = new();

    private const int MaxFeedItems = 300;

    // ── Status ─────────────────────────────────────────────────────────────────

    private string _statusText = "Initializing…";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _isWatching;
    public bool IsWatching
    {
        get => _isWatching;
        set { _isWatching = value; OnPropertyChanged(); }
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    public ICommand AddSpeakerCommand       { get; }
    public ICommand EditSpeakerCommand      { get; }
    public ICommand RemoveSpeakerCommand    { get; }
    public ICommand ToggleMonitoringCommand { get; }

    // ── Constructor ────────────────────────────────────────────────────────────

    public MainViewModel()
    {
        _kokoro         = new KokoroService(AppConfig.TtsFolder);
        _ttsQueue       = new TtsQueueService(_kokoro);
        _watcher        = new EqLogWatcher(AppConfig.Current.EqLogPath);
        _processMonitor = new EqProcessMonitor();

        foreach (var sp in AppConfig.Current.Speakers)
            Speakers.Add(new SpeakerItem(sp));

        // Wire TTS state → sidebar highlight
        _ttsQueue.SpeakingChanged += name => Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var s in Speakers)
                s.IsSpeaking = s.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        });

        _ttsQueue.PendingChanged += (name, count) => Application.Current.Dispatcher.Invoke(() =>
        {
            var item = Speakers.FirstOrDefault(s =>
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (item != null) item.PendingCount = count;
        });

        _watcher.LineReceived += OnLineReceived;
        _watcher.Error        += msg => StatusText = $"Log error: {msg}";

        _processMonitor.EqClosed += OnEqClosed;

        AddSpeakerCommand       = new RelayCommand(_ => OnAddSpeaker());
        EditSpeakerCommand      = new RelayCommand(o => OnEditSpeaker(o as SpeakerItem));
        RemoveSpeakerCommand    = new RelayCommand(o => OnRemoveSpeaker(o as SpeakerItem));
        ToggleMonitoringCommand = new RelayCommand(_ => ToggleMonitoring());

        if (KokoroService.IsModelReady(AppConfig.TtsFolder))
            _kokoro.Initialize();

        _watcher.Start();
        _processMonitor.Start();
        IsWatching = true;
        StatusText = $"Watching: {Path.GetFileName(AppConfig.Current.EqLogPath)}";
    }

    // ── Log processing ─────────────────────────────────────────────────────────

    private void OnLineReceived(string line)
    {
        var result = EqLogParser.Parse(line, AppConfig.Current.PlayerName);

        if (result.CombatEvent != null)
            Combat.ProcessEvent(result.CombatEvent);

        if (result.LogEvent != null)
            HandleLogEvent(result.LogEvent);
    }

    private void HandleLogEvent(LogEvent ev)
    {
        // Map log event type → activity item tag text
        var tag = ev.Type == LogEventType.Chat
            ? ev.Speaker.ToUpperInvariant()
            : ev.Type.ToString().ToUpperInvariant();

        var item = new ActivityItem
        {
            Type = ev.Type,
            Tag  = tag,
            Text = ev.Text,
            Time = ev.Time.ToString("HH:mm"),
        };

        Application.Current.Dispatcher.Invoke(() =>
        {
            ActivityFeed.Insert(0, item);
            if (ActivityFeed.Count > MaxFeedItems) ActivityFeed.RemoveAt(ActivityFeed.Count - 1);
        });

        // Route chat to TTS if the speaker is in our sidebar
        if (ev.Type == LogEventType.Chat)
        {
            var speakerItem = Speakers.FirstOrDefault(s =>
                s.Profile.Enabled &&
                s.Name.Equals(ev.Speaker, StringComparison.OrdinalIgnoreCase));
            if (speakerItem != null)
                _ttsQueue.Enqueue(speakerItem.Profile, ev.Text);
        }

        // Route feed events to TTS if a voice is configured for that event type
        var settings = AppConfig.Current;
        VoiceProfile? eventVoice = ev.Type switch
        {
            LogEventType.Zone       => settings.ZoneVoice,
            LogEventType.Experience => settings.ExpVoice,
            LogEventType.Loot       => settings.LootVoice,
            _                       => null,
        };
        if (eventVoice?.IsEnabled == true)
        {
            var narr = new SpeakerProfile { Name = "_event", VoiceProfile = eventVoice, Enabled = true };
            _ttsQueue.Enqueue(narr, ev.Text);
        }
    }

    // ── EQ process events ──────────────────────────────────────────────────────

    private void OnEqClosed()
    {
        if (!AppConfig.Current.ArchiveOnEqExit) return;
        var folder = string.IsNullOrWhiteSpace(AppConfig.Current.ArchiveFolder)
            ? AppConfig.ArchiveFolder
            : AppConfig.Current.ArchiveFolder;
        // Grace period in case of crash/restart
        Task.Delay(60_000).ContinueWith(_ =>
            LogArchiveService.Archive(AppConfig.Current.EqLogPath, folder));
    }

    // ── Monitoring toggle ──────────────────────────────────────────────────────

    private void ToggleMonitoring()
    {
        if (IsWatching)
        {
            _watcher.Stop();
            IsWatching = false;
            StatusText = "Monitoring paused.";
        }
        else
        {
            _watcher.Start();
            IsWatching = true;
            StatusText = $"Watching: {Path.GetFileName(AppConfig.Current.EqLogPath)}";
        }
    }

    // ── Speaker CRUD ───────────────────────────────────────────────────────────

    private void OnAddSpeaker()
    {
        var profile = new SpeakerProfile();
        var win     = new SpeakerEditWindow(profile, isNew: true);
        if (win.ShowDialog() != true) return;
        AppConfig.Current.Speakers.Add(profile);
        AppConfig.Save();
        Speakers.Add(new SpeakerItem(profile));
    }

    private void OnEditSpeaker(SpeakerItem? item)
    {
        if (item == null) return;
        var win = new SpeakerEditWindow(item.Profile, isNew: false);
        win.ShowDialog();
        AppConfig.Save();
    }

    private void OnRemoveSpeaker(SpeakerItem? item)
    {
        if (item == null) return;
        var result = MessageBox.Show(
            $"Remove {item.Name} from speakers?", "GukVoice",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        AppConfig.Current.Speakers.Remove(item.Profile);
        AppConfig.Save();
        Speakers.Remove(item);
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _watcher.Dispose();
        _processMonitor.Dispose();
        _ttsQueue.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
