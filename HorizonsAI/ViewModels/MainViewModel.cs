using HorizonsAI.Models;
using HorizonsAI.Services;

namespace HorizonsAI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly HttpClient        _http   = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly OpenRouterService _openRouter;
    private readonly Dictionary<string, ObservableCollection<ChatMessage>> _conversations = new();

    // ── Sidebar ────────────────────────────────────────────────────────────────

    public ObservableCollection<CategoryGroup> Categories { get; } = new();
    public ObservableCollection<PartyItem>     Parties    { get; } = new();

    // ── Selected character ─────────────────────────────────────────────────────

    private CharacterItem? _selectedCharacter;
    public CharacterItem? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (_selectedCharacter == value) return;
            if (_selectedCharacter != null) _selectedCharacter.IsSelected = false;
            _selectedCharacter = value;
            if (_selectedCharacter != null)
            {
                _selectedCharacter.IsSelected = true;
                if (_selectedParty != null)
                {
                    _selectedParty.IsSelected = false;
                    _selectedParty = null;
                    OnPropertyChanged(nameof(SelectedParty));
                }
            }
            NotifyActiveChanged();
            SwitchConversation();
        }
    }

    public ICommand SelectCharacterCommand { get; }

    // ── Selected party ─────────────────────────────────────────────────────────

    private PartyItem? _selectedParty;
    public PartyItem? SelectedParty
    {
        get => _selectedParty;
        set
        {
            if (_selectedParty == value) return;
            if (_selectedParty != null) _selectedParty.IsSelected = false;
            _selectedParty = value;
            if (_selectedParty != null)
            {
                _selectedParty.IsSelected = true;
                if (_selectedCharacter != null)
                {
                    _selectedCharacter.IsSelected = false;
                    _selectedCharacter = null;
                    OnPropertyChanged(nameof(SelectedCharacter));
                }
            }
            NotifyActiveChanged();
            SwitchConversation();
        }
    }

    public ICommand SelectPartyCommand { get; }

    // ── Active header properties ───────────────────────────────────────────────

    public BitmapImage? ActivePortrait
        => _selectedCharacter?.Portrait
           ?? _selectedParty?.Members.FirstOrDefault()?.Portrait;

    public string ActiveCharacterName
        => _selectedCharacter?.DisplayName
           ?? _selectedParty?.DisplayName
           ?? "Select a character or party";

    public string ActiveCategoryBadge
        => _selectedCharacter?.CategoryBadge
           ?? (_selectedParty != null ? "PARTY" : "");

    public bool HasActiveCharacter => _selectedCharacter != null || _selectedParty != null;
    public bool IsPartyActive      => _selectedParty != null;

    public IReadOnlyList<CharacterItem> ActivePartyMembers
        => (IReadOnlyList<CharacterItem>?)_selectedParty?.Members
           ?? Array.Empty<CharacterItem>();

    // ── Play-as ────────────────────────────────────────────────────────────────

    private CharacterItem? _playAsCharacter;
    public CharacterItem? PlayAsCharacter
    {
        get => _playAsCharacter;
        private set
        {
            _playAsCharacter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayAsName));
            OnPropertyChanged(nameof(PlayAsPortrait));
            OnPropertyChanged(nameof(PlayAsInitial));
        }
    }

    public string       PlayAsName    => _playAsCharacter?.Character.Name ?? AppConfig.Current.SpeakerName;
    public BitmapImage? PlayAsPortrait => _playAsCharacter?.Portrait;
    public string       PlayAsInitial  => PlayAsName.Length > 0 ? PlayAsName[0].ToString().ToUpper() : "P";

    public void SetPlayAs(CharacterItem? item) => PlayAsCharacter = item;

    // ── All characters flat (for play-as picker) ───────────────────────────────

    private List<CharacterItem> _allCharactersFlat = new();
    public IReadOnlyList<CharacterItem> AllCharactersFlat => _allCharactersFlat;

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

    private string _statusText = "";
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

    // ── Scroll signal ──────────────────────────────────────────────────────────

    public event Action? ScrollToBottom;

    // ── Constructor ────────────────────────────────────────────────────────────

    public MainViewModel()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HorizonsAI/1.0");
        _openRouter = new OpenRouterService(_http);

        SendCommand = new RelayCommand(
            async _ => await SendMessageAsync(),
            _ => !IsSending && HasActiveCharacter && !string.IsNullOrWhiteSpace(InputText));

        SelectCharacterCommand = new RelayCommand(
            p => { if (p is CharacterItem item) SelectedCharacter = item; return Task.CompletedTask; });

        SelectPartyCommand = new RelayCommand(
            p => { if (p is PartyItem item) SelectedParty = item; return Task.CompletedTask; });
    }

    // ── Character management ───────────────────────────────────────────────────

    public void LoadCharacters()
    {
        var previousCharId  = _selectedCharacter?.Character.Id;
        var previousPlayAsId = _playAsCharacter?.Character.Id;
        var allChars        = CharacterService.LoadAll();

        Categories.Clear();

        foreach (var group in allChars.GroupBy(c => c.Category))
        {
            var cat = new CategoryGroup(group.Key);
            foreach (var c in group)
                cat.Characters.Add(new CharacterItem(c));
            Categories.Add(cat);
        }

        _allCharactersFlat = Categories.SelectMany(g => g.Characters).ToList();
        OnPropertyChanged(nameof(AllCharactersFlat));

        // Restore character selection
        if (previousCharId != null)
        {
            var match = _allCharactersFlat.FirstOrDefault(i => i.Character.Id == previousCharId);
            if (match != null) SelectedCharacter = match;
        }

        // Restore play-as selection (or clear if the character was deleted)
        PlayAsCharacter = previousPlayAsId != null
            ? _allCharactersFlat.FirstOrDefault(i => i.Character.Id == previousPlayAsId)
            : null;

        StatusText = Categories.Count == 0
            ? "No characters yet — click [+] to add one."
            : "";

        // Refresh party member references against the new CharacterItem instances
        LoadParties();
    }

    public void DeleteCharacter(CharacterItem item)
    {
        CharacterService.Delete(item.Character);
        if (SelectedCharacter == item) SelectedCharacter = null;
        LoadCharacters();
    }

    // ── Party management ───────────────────────────────────────────────────────

    public void LoadParties()
    {
        var allCharItems = Categories.SelectMany(g => g.Characters).ToList();
        var prevId       = _selectedParty?.Party.Id;

        Parties.Clear();
        foreach (var p in PartyService.LoadAll())
        {
            var item = new PartyItem(p);
            item.ResolveMembers(allCharItems);
            Parties.Add(item);
        }

        // Restore party selection
        if (prevId != null)
        {
            var match = Parties.FirstOrDefault(p => p.Party.Id == prevId);
            if (match != null)
            {
                match.IsSelected = true;
                _selectedParty   = match;
                NotifyActiveChanged();
            }
        }
    }

    public void DeleteParty(PartyItem item)
    {
        PartyService.Delete(item.Party);
        if (SelectedParty == item) SelectedParty = null;
        LoadParties();
    }

    public void OnSettingsChanged() { }

    // ── Conversation management ────────────────────────────────────────────────

    private void SwitchConversation()
    {
        var key = _selectedCharacter?.Character.Id
                  ?? (_selectedParty != null ? $"party_{_selectedParty.Party.Id}" : null);
        if (key is null) return;
        if (!_conversations.ContainsKey(key))
            _conversations[key] = new ObservableCollection<ChatMessage>();
        Messages   = _conversations[key];
        StatusText = "";
        ScrollToBottom?.Invoke();
    }

    // ── Send message ───────────────────────────────────────────────────────────

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var text = InputText.Trim();
        InputText = "";
        IsSending = true;

        Messages.Add(new ChatMessage
        {
            Text       = text,
            IsPlayer   = true,
            SenderName = PlayAsName,
            Portrait   = PlayAsPortrait,
            Timestamp  = DateTime.Now,
        });
        ScrollToBottom?.Invoke();

        try
        {
            if (_selectedCharacter != null)
                await SendToCharacterAsync(_selectedCharacter, text);
            else if (_selectedParty != null)
                await SendToPartyAsync(_selectedParty, text);
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

    private async Task SendToCharacterAsync(CharacterItem charItem, string text)
    {
        StatusText = $"{charItem.DisplayName} is thinking…";
        var lines      = await _openRouter.ChatAsync(charItem.Character, Messages.SkipLast(0), text, _playAsCharacter?.Character);
        var charName   = charItem.DisplayName;
        var voiceModel = charItem.Character.VoiceModel;

        foreach (var line in lines)
        {
            Messages.Add(new ChatMessage
            {
                Text       = line,
                IsPlayer   = false,
                SenderName = charName,
                Portrait   = charItem.Portrait,
                Timestamp  = DateTime.Now,
            });
        }
        ScrollToBottom?.Invoke();
        StatusText = "";

        if (IsVoiceEnabled)
            _ = PiperService.SpeakLinesAsync(lines, charName, voiceModel);
    }

    private async Task SendToPartyAsync(PartyItem partyItem, string text)
    {
        StatusText = $"{partyItem.DisplayName} is responding…";

        var members  = partyItem.Members.Select(m => m.Character);
        var replies  = await _openRouter.ChatPartyAsync(partyItem.Party, members, Messages.SkipLast(0), text, _playAsCharacter?.Character);

        var portraitMap = partyItem.Members.ToDictionary(m => m.Character.Name, m => m.Portrait);
        var voiceMap    = partyItem.Members.ToDictionary(m => m.Character.Name, m => m.Character.VoiceModel);

        foreach (var (name, msg) in replies)
        {
            portraitMap.TryGetValue(name, out var portrait);
            Messages.Add(new ChatMessage
            {
                Text       = msg,
                IsPlayer   = false,
                SenderName = name,
                Portrait   = portrait,
                Timestamp  = DateTime.Now,
            });
        }
        ScrollToBottom?.Invoke();
        StatusText = "";

        if (IsVoiceEnabled)
        {
            _ = Task.Run(async () =>
            {
                foreach (var (name, msg) in replies)
                {
                    voiceMap.TryGetValue(name, out var vm);
                    await PiperService.SpeakLinesAsync([msg], name, vm);
                }
            });
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void NotifyActiveChanged()
    {
        OnPropertyChanged(nameof(ActivePortrait));
        OnPropertyChanged(nameof(ActiveCharacterName));
        OnPropertyChanged(nameof(ActiveCategoryBadge));
        OnPropertyChanged(nameof(HasActiveCharacter));
        OnPropertyChanged(nameof(IsPartyActive));
        OnPropertyChanged(nameof(ActivePartyMembers));
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
