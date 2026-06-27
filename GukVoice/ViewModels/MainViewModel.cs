using GukVoice.Models;
using GukVoice.Services;

namespace GukVoice.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly KokoroService    _kokoro;
    private readonly TtsQueueService  _ttsQueue;
    private readonly EqLogWatcher     _watcher;
    private readonly EqProcessMonitor _processMonitor;
    private readonly EqWindowTracker  _windowTracker;

    public ObservableCollection<SpeakerItem>  Speakers     { get; } = new();
    public ObservableCollection<ActivityItem> ActivityFeed { get; } = new();
    public CombatViewModel                    Combat       { get; } = new();
    public FctViewModel                       Fct          { get; } = new();

    public event Action<Rect>? EqWindowMoved;
    public event Action?       EqStarted;
    public event Action?       EqClosed;

    public bool IsEqRunning => _processMonitor.IsRunning;

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
    public ICommand OpenSettingsCommand     { get; }
    public ICommand ReloadTtsCommand        { get; }
    public ICommand ArchiveLogCommand       { get; }

    // ── Constructor ────────────────────────────────────────────────────────────

    public MainViewModel()
    {
        _kokoro         = new KokoroService(AppConfig.TtsFolder);
        _ttsQueue       = new TtsQueueService(_kokoro);
        _watcher        = new EqLogWatcher(AppConfig.Current.EqLogPath);
        _processMonitor = new EqProcessMonitor();
        _windowTracker  = new EqWindowTracker();
        _windowTracker.RectChanged += rect => EqWindowMoved?.Invoke(rect);

        _processMonitor.EqStarted += () => EqStarted?.Invoke();
        _processMonitor.EqClosed  += () => EqClosed?.Invoke();

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
        _watcher.FileFound    += () => Application.Current.Dispatcher.Invoke(() =>
        {
            IsWatching = true;
            StatusText = $"Watching: {Path.GetFileName(AppConfig.Current.EqLogPath)}";
        });

        _processMonitor.EqClosed += OnEqClosed;

        AddSpeakerCommand       = new RelayCommand(_ => OnAddSpeaker());
        EditSpeakerCommand      = new RelayCommand(o => OnEditSpeaker(o as SpeakerItem));
        RemoveSpeakerCommand    = new RelayCommand(o => OnRemoveSpeaker(o as SpeakerItem));
        ToggleMonitoringCommand = new RelayCommand(_ => ToggleMonitoring());
        OpenSettingsCommand     = new RelayCommand(_ => new SettingsWindow().ShowDialog());
        ReloadTtsCommand        = new RelayCommand(_ => OnReloadTts());
        ArchiveLogCommand       = new RelayCommand(_ => OnArchiveLog());

        if (KokoroService.IsModelReady(AppConfig.TtsFolder))
        {
            try
            {
                var settings = AppConfig.Current;
                var allProfiles = settings.Speakers
                    .Select(s => s.VoiceProfile)
                    .Concat(new[]
                    {
                        settings.NarratorVoice,
                        settings.ZoneVoice,
                        settings.ExpVoice,
                        settings.LootVoice,
                    }.OfType<VoiceProfile>());
                _kokoro.Initialize(allProfiles);
                StatusText = _kokoro.BlendDiagnostic;
            }
            catch (Exception ex) { StatusText = $"TTS init failed: {ex.Message}"; }
        }

        _watcher.Start();
        _processMonitor.Start();
        _windowTracker.Start();
        IsWatching = true;

        var watchFile = Path.GetFileName(AppConfig.Current.EqLogPath);
        StatusText = _kokoro.IsInitialized
            ? $"Watching: {watchFile}"
            : $"Watching: {watchFile}  ⚠ TTS model not found — copy model files to data\\tts\\";
    }

    // ── Log processing ─────────────────────────────────────────────────────────

    private void OnLineReceived(string line)
    {
        var result = EqLogParser.Parse(line, AppConfig.Current.PlayerName);

        if (result.CombatEvent != null)
        {
            Combat.ProcessEvent(result.CombatEvent);
            Fct.ProcessEvent(result.CombatEvent);
        }

        if (result.LogEvent != null)
            HandleLogEvent(result.LogEvent);
    }

    private void HandleLogEvent(LogEvent ev)
    {
        var tag = ev.Type == LogEventType.Chat
            ? ev.Speaker.ToUpperInvariant()
            : ev.Type.ToString().ToUpperInvariant();

        Application.Current.Dispatcher.Invoke(() =>
        {
            // Speaker lookup first so IsMatched can colour the feed item
            SpeakerItem? speakerItem = null;
            if (ev.Type == LogEventType.Chat)
                speakerItem = Speakers.FirstOrDefault(s =>
                    s.Profile.Enabled &&
                    s.Name.Equals(ev.Speaker, StringComparison.OrdinalIgnoreCase));

            var item = new ActivityItem
            {
                Type      = ev.Type,
                Tag       = tag,
                Text      = ev.Text,
                Time      = ev.Time.ToString("HH:mm"),
                IsMatched = speakerItem != null,
            };

            ActivityFeed.Insert(0, item);
            if (ActivityFeed.Count > MaxFeedItems) ActivityFeed.RemoveAt(ActivityFeed.Count - 1);

            if (speakerItem != null)
                _ttsQueue.Enqueue(speakerItem.Profile, ev.Text);

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
        });
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

    // ── Log archive ───────────────────────────────────────────────────────────

    private void OnArchiveLog()
    {
        var settings = AppConfig.Current;
        var folder   = string.IsNullOrWhiteSpace(settings.ArchiveFolder)
            ? AppConfig.ArchiveFolder
            : settings.ArchiveFolder;

        StatusText = "Archiving log…";
        _watcher.Stop();
        IsWatching = false;

        try
        {
            LogArchiveService.Archive(settings.EqLogPath, folder);
        }
        catch (Exception ex)
        {
            StatusText = $"Archive failed: {ex.Message}";
            return;
        }

        if (_processMonitor.IsRunning)
        {
            StatusText = "Log archived — waiting for EQ to create new log…";
            _watcher.WaitForNewFile(() => _processMonitor.IsRunning);
        }
        else
        {
            StatusText = "Log archived — launch EQ to start a new log";
        }
    }

    // ── TTS reload ────────────────────────────────────────────────────────────

    private void OnReloadTts()
    {
        StatusText = "Reloading TTS…";
        try
        {
            var settings    = AppConfig.Current;
            var allProfiles = settings.Speakers
                .Select(s => s.VoiceProfile)
                .Concat(new[] { settings.NarratorVoice, settings.ZoneVoice,
                                settings.ExpVoice,      settings.LootVoice }
                    .OfType<VoiceProfile>());
            _kokoro.Reinitialize(allProfiles);
            StatusText = _kokoro.BlendDiagnostic;
        }
        catch (Exception ex) { StatusText = $"TTS reload failed: {ex.Message}"; }
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
        _windowTracker.Dispose();
        _ttsQueue.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
