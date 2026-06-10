using GukChat.Models;
using GukChat.Services;

namespace GukChat.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly HttpClient  _http   = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly BotAiService _botAi;
    private readonly Dictionary<string, ObservableCollection<ChatMessage>> _conversations = new();

    // ── Character sidebar ──────────────────────────────────────────────────────

    public ObservableCollection<CharacterItem> Characters { get; } = new();

    private CharacterItem? _selectedCharacter;
    public CharacterItem? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (_selectedCharacter == value) return;
            _selectedCharacter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActivePortrait));
            OnPropertyChanged(nameof(ActiveCharacterName));
            OnPropertyChanged(nameof(ActiveCategoryBadge));
            OnPropertyChanged(nameof(HasActiveCharacter));
            SwitchConversation();
        }
    }

    // ── Active character header ────────────────────────────────────────────────

    public BitmapImage? ActivePortrait      => _selectedCharacter?.Portrait;
    public string       ActiveCharacterName => _selectedCharacter?.DisplayName ?? "Select a character";
    public string       ActiveCategoryBadge => _selectedCharacter?.CategoryBadge ?? "";
    public bool         HasActiveCharacter  => _selectedCharacter != null;

    // ── Messages ───────────────────────────────────────────────────────────────

    private ObservableCollection<ChatMessage> _messages = new();
    public ObservableCollection<ChatMessage> Messages
    {
        get => _messages;
        private set { _messages = value; OnPropertyChanged(); }
    }

    // ── Input / status ─────────────────────────────────────────────────────────

    private string _inputText = "";
    public string InputText
    {
        get => _inputText;
        set { _inputText = value; OnPropertyChanged(); }
    }

    private bool _isSending;
    public bool IsSending
    {
        get => _isSending;
        set
        {
            _isSending = value;
            OnPropertyChanged();
            Application.Current.Dispatcher.InvokeAsync(CommandManager.InvalidateRequerySuggested);
        }
    }

    private string _statusText = "Connecting…";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStatus)); }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_statusText);

    private bool _isVoiceEnabled;
    public bool IsVoiceEnabled
    {
        get => _isVoiceEnabled;
        set { _isVoiceEnabled = value; OnPropertyChanged(); }
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    public ICommand SendCommand { get; }

    // ── Scroll-to-bottom signal ────────────────────────────────────────────────

    public event Action? ScrollToBottom;

    // ── Constructor ────────────────────────────────────────────────────────────

    public MainViewModel()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GukChat/1.0");
        _botAi = new BotAiService(_http);
        SendCommand = new RelayCommand(
            async _ => await SendMessageAsync(),
            _ => !IsSending && SelectedCharacter != null && !string.IsNullOrWhiteSpace(InputText));
    }

    // ── Startup ────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        try
        {
            var chars = await _botAi.GetCharactersAsync();
            foreach (var c in chars)
                Characters.Add(new CharacterItem(c));
            StatusText = Characters.Count > 0 ? "" : "No characters found. Add some in bot_ai.";
            _ = LoadPortraitsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Could not reach bot_ai: {ex.Message}";
        }
    }

    private async Task LoadPortraitsAsync()
    {
        foreach (var item in Characters.ToList())
        {
            if (item.Character.PortraitUrl is null) continue;
            item.Portrait = await _botAi.GetPortraitAsync(item.Character.PortraitUrl);
            if (item == _selectedCharacter)
                OnPropertyChanged(nameof(ActivePortrait));
        }
    }

    // ── Conversation management ────────────────────────────────────────────────

    private void SwitchConversation()
    {
        if (_selectedCharacter is null) return;
        var key = _selectedCharacter.Character.NpcName;
        if (!_conversations.ContainsKey(key))
            _conversations[key] = new ObservableCollection<ChatMessage>();
        Messages   = _conversations[key];
        StatusText = "";
        ScrollToBottom?.Invoke();
    }

    // ── Send message ───────────────────────────────────────────────────────────

    private async Task SendMessageAsync()
    {
        if (SelectedCharacter is null || string.IsNullOrWhiteSpace(InputText)) return;
        var text = InputText.Trim();
        InputText = "";
        IsSending = true;

        Messages.Add(new ChatMessage
        {
            Text       = text,
            IsPlayer   = true,
            SenderName = AppConfig.SpeakerName,
            Timestamp  = DateTime.Now,
        });
        ScrollToBottom?.Invoke();

        try
        {
            StatusText = $"{SelectedCharacter.DisplayName} is thinking…";
            var lines = await _botAi.ChatAsync(SelectedCharacter.Character, text);

            foreach (var line in lines.DefaultIfEmpty("…"))
            {
                Messages.Add(new ChatMessage
                {
                    Text       = line,
                    IsPlayer   = false,
                    SenderName = SelectedCharacter.DisplayName,
                    Portrait   = SelectedCharacter.Portrait,
                    Timestamp  = DateTime.Now,
                });
            }
            ScrollToBottom?.Invoke();
            StatusText = "";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSending = false;
        }
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
