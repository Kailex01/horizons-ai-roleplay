using HorizonsAI.Models;
using HorizonsAI.Services;

namespace HorizonsAI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly HttpClient        _http   = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly OpenRouterService _openRouter;
    private readonly Dictionary<string, ObservableCollection<ChatMessageVm>> _conversations = new();
    private readonly Dictionary<string, string> _memory = new();
    private Lorebook _lorebook = new();

    private const int SummarizeThreshold = 40;
    private const int KeepRecentCount    = 20;

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

    public string       PlayAsName     => _playAsCharacter?.Character.Name ?? AppConfig.Current.SpeakerName;
    public BitmapImage? PlayAsPortrait => _playAsCharacter?.Portrait;
    public string       PlayAsInitial  => PlayAsName.Length > 0 ? PlayAsName[0].ToString().ToUpper() : "P";

    public void SetPlayAs(CharacterItem? item) => PlayAsCharacter = item;

    // ── All characters flat (for play-as picker) ───────────────────────────────

    private List<CharacterItem> _allCharactersFlat = new();
    public IReadOnlyList<CharacterItem> AllCharactersFlat => _allCharactersFlat;

    // ── Messages ───────────────────────────────────────────────────────────────

    private ObservableCollection<ChatMessageVm> _messages = new();
    public ObservableCollection<ChatMessageVm> Messages
    {
        get => _messages;
        private set
        {
            _messages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRegenerate));
        }
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
            OnPropertyChanged(nameof(CanRegenerate));
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

    // ── Author's note ──────────────────────────────────────────────────────────

    private bool _isAuthorsNoteOpen;
    public bool IsAuthorsNoteOpen
    {
        get => _isAuthorsNoteOpen;
        set { _isAuthorsNoteOpen = value; OnPropertyChanged(); }
    }

    private string _authorsNote = "";
    public string AuthorsNote
    {
        get => _authorsNote;
        set
        {
            _authorsNote = value;
            OnPropertyChanged();
            AppConfig.Current.AuthorsNote = value;
            AppConfig.Save();
        }
    }

    // ── Regenerate ────────────────────────────────────────────────────────────

    public bool CanRegenerate => !_isSending && HasActiveCharacter && _messages.Any(vm => vm.IsPlayer);

    // ── Commands ───────────────────────────────────────────────────────────────

    public ICommand SendCommand       { get; }
    public ICommand RegenerateCommand { get; }
    public ICommand EditBeginCommand  { get; }
    public ICommand EditCommitCommand { get; }
    public ICommand EditCancelCommand { get; }

    // ── Scroll signal ──────────────────────────────────────────────────────────

    public event Action? ScrollToBottom;

    // ── Constructor ────────────────────────────────────────────────────────────

    public MainViewModel()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HorizonsAI/1.0");
        _openRouter  = new OpenRouterService(_http);
        _authorsNote = AppConfig.Current.AuthorsNote;

        SendCommand = new RelayCommand(
            async _ => await SendMessageAsync(),
            _ => !IsSending && HasActiveCharacter && !string.IsNullOrWhiteSpace(InputText));

        RegenerateCommand = new RelayCommand(
            async _ => await RegenerateAsync(),
            _ => CanRegenerate);

        EditBeginCommand = new RelayCommand(p =>
        {
            if (p is ChatMessageVm vm) vm.BeginEdit();
            return Task.CompletedTask;
        });

        EditCommitCommand = new RelayCommand(p =>
        {
            if (p is ChatMessageVm vm)
            {
                vm.CommitEdit();
                var key = ConversationKey();
                if (key != null) AutoSave(key);
            }
            return Task.CompletedTask;
        });

        EditCancelCommand = new RelayCommand(p =>
        {
            if (p is ChatMessageVm vm) vm.CancelEdit();
            return Task.CompletedTask;
        });

        SelectCharacterCommand = new RelayCommand(
            p => { if (p is CharacterItem item) SelectedCharacter = item; return Task.CompletedTask; });

        SelectPartyCommand = new RelayCommand(
            p => { if (p is PartyItem item) SelectedParty = item; return Task.CompletedTask; });
    }

    // ── Character management ───────────────────────────────────────────────────

    public void LoadCharacters()
    {
        var previousCharId   = _selectedCharacter?.Character.Id;
        var previousPlayAsId = _playAsCharacter?.Character.Id;
        var allChars         = CharacterService.LoadAll();

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

        if (previousCharId != null)
        {
            var match = _allCharactersFlat.FirstOrDefault(i => i.Character.Id == previousCharId);
            if (match != null) SelectedCharacter = match;
        }

        PlayAsCharacter = previousPlayAsId != null
            ? _allCharactersFlat.FirstOrDefault(i => i.Character.Id == previousPlayAsId)
            : null;

        StatusText = Categories.Count == 0
            ? "No characters yet — click [+] to add one."
            : "";

        LoadLorebook();
        LoadParties();
    }

    public void DeleteCharacter(CharacterItem item)
    {
        var key = item.Character.Id;
        CharacterService.Delete(item.Character);
        ChatLogService.Delete(key);
        _conversations.Remove(key);
        _memory.Remove(key);
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
        var key = $"party_{item.Party.Id}";
        PartyService.Delete(item.Party);
        ChatLogService.Delete(key);
        _conversations.Remove(key);
        _memory.Remove(key);
        if (SelectedParty == item) SelectedParty = null;
        LoadParties();
    }

    public void LoadLorebook() => _lorebook = LorebookService.Load();

    public void OnSettingsChanged()
    {
        _authorsNote = AppConfig.Current.AuthorsNote;
        OnPropertyChanged(nameof(AuthorsNote));
    }

    // ── Conversation management ────────────────────────────────────────────────

    private void SwitchConversation()
    {
        var key = ConversationKey();
        if (key is null) return;

        if (!_conversations.ContainsKey(key))
            LoadConversationFromDisk(key);

        Messages   = _conversations[key];
        StatusText = "";
        ScrollToBottom?.Invoke();
    }

    private void LoadConversationFromDisk(string key)
    {
        var state = ChatLogService.Load(key);
        _memory[key] = state.Memory ?? "";

        var loaded = new ObservableCollection<ChatMessageVm>();
        foreach (var dto in state.Messages)
        {
            loaded.Add(new ChatMessageVm(new ChatMessage
            {
                Text         = dto.Text,
                IsPlayer     = dto.IsPlayer,
                IsSummary    = dto.IsSummary,
                SenderName   = dto.SenderName,
                PortraitFile = dto.PortraitFile,
                Portrait     = !string.IsNullOrEmpty(dto.PortraitFile)
                               ? PortraitService.Load(dto.PortraitFile) : null,
                Timestamp    = dto.Timestamp,
            }));
        }
        _conversations[key] = loaded;
    }

    private string? ConversationKey()
        => _selectedCharacter?.Character.Id
           ?? (_selectedParty != null ? $"party_{_selectedParty.Party.Id}" : null);

    // ── Send message ───────────────────────────────────────────────────────────

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var key = ConversationKey();
        if (key is null) return;

        var text = InputText.Trim();
        InputText = "";
        IsSending = true;

        Messages.Add(new ChatMessageVm(new ChatMessage
        {
            Text         = text,
            IsPlayer     = true,
            SenderName   = PlayAsName,
            Portrait     = PlayAsPortrait,
            PortraitFile = _playAsCharacter?.Character.Portrait,
            Timestamp    = DateTime.Now,
        }));
        ScrollToBottom?.Invoke();

        try
        {
            if (_selectedCharacter != null)
                await SendToCharacterAsync(_selectedCharacter, key, text);
            else if (_selectedParty != null)
                await SendToPartyAsync(_selectedParty, key, text);
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

    private async Task RegenerateAsync()
    {
        var key = ConversationKey();
        if (key is null) return;

        int lastPlayerIdx = -1;
        for (int i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i].IsPlayer) { lastPlayerIdx = i; break; }
        }
        if (lastPlayerIdx < 0) return;

        var lastText = Messages[lastPlayerIdx].Message.Text;

        while (Messages.Count > lastPlayerIdx + 1)
            Messages.RemoveAt(Messages.Count - 1);

        IsSending = true;
        try
        {
            if (_selectedCharacter != null)
                await SendToCharacterAsync(_selectedCharacter, key, lastText);
            else if (_selectedParty != null)
                await SendToPartyAsync(_selectedParty, key, lastText);
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

    private async Task SendToCharacterAsync(CharacterItem charItem, string key, string text)
    {
        StatusText = $"{charItem.DisplayName} is thinking…";

        var memory = GetMemory(key);
        var lore   = OpenRouterService.MatchLore(Messages.Select(vm => vm.Message), text, _lorebook.Entries);
        var lines  = await _openRouter.ChatAsync(
            charItem.Character,
            Messages.SkipLast(1).Select(vm => vm.Message),
            text,
            _playAsCharacter?.Character,
            memory,
            lore,
            _authorsNote);

        foreach (var line in lines)
        {
            Messages.Add(new ChatMessageVm(new ChatMessage
            {
                Text         = line,
                IsPlayer     = false,
                SenderName   = charItem.DisplayName,
                Portrait     = charItem.Portrait,
                PortraitFile = charItem.Character.Portrait,
                Timestamp    = DateTime.Now,
            }));
        }
        ScrollToBottom?.Invoke();
        StatusText = "";
        OnPropertyChanged(nameof(CanRegenerate));

        if (IsVoiceEnabled)
            _ = PiperService.SpeakLinesAsync(lines, charItem.DisplayName, charItem.Character.VoiceModel);

        AutoSave(key);
        if (Messages.Count > SummarizeThreshold)
            await TrySummarizeAsync(key);
    }

    private async Task SendToPartyAsync(PartyItem partyItem, string key, string text)
    {
        StatusText = $"{partyItem.DisplayName} is responding…";

        var memory   = GetMemory(key);
        var lore     = OpenRouterService.MatchLore(Messages.Select(vm => vm.Message), text, _lorebook.Entries);
        var members  = partyItem.Members.Select(m => m.Character);
        var replies  = await _openRouter.ChatPartyAsync(
            partyItem.Party,
            members,
            Messages.SkipLast(1).Select(vm => vm.Message),
            text,
            _playAsCharacter?.Character,
            memory,
            lore,
            _authorsNote);

        var portraitMap = partyItem.Members.ToDictionary(m => m.Character.Name, m => m.Portrait);
        var fileMap     = partyItem.Members.ToDictionary(m => m.Character.Name, m => m.Character.Portrait);
        var voiceMap    = partyItem.Members.ToDictionary(m => m.Character.Name, m => m.Character.VoiceModel);

        foreach (var (name, msg) in replies)
        {
            portraitMap.TryGetValue(name, out var portrait);
            fileMap.TryGetValue(name, out var portraitFile);
            Messages.Add(new ChatMessageVm(new ChatMessage
            {
                Text         = msg,
                IsPlayer     = false,
                SenderName   = name,
                Portrait     = portrait,
                PortraitFile = portraitFile,
                Timestamp    = DateTime.Now,
            }));
        }
        ScrollToBottom?.Invoke();
        StatusText = "";
        OnPropertyChanged(nameof(CanRegenerate));

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

        AutoSave(key);
        if (Messages.Count > SummarizeThreshold)
            await TrySummarizeAsync(key);
    }

    // ── Persistence ────────────────────────────────────────────────────────────

    private void AutoSave(string key)
    {
        if (!_conversations.TryGetValue(key, out var vms)) return;
        _memory.TryGetValue(key, out var memory);

        var state = new ConversationState
        {
            Memory   = string.IsNullOrEmpty(memory) ? null : memory,
            Messages = vms.Select(vm => new ChatMessageDto
            {
                Text         = vm.Message.Text,
                IsPlayer     = vm.Message.IsPlayer,
                IsSummary    = vm.Message.IsSummary,
                SenderName   = vm.Message.SenderName,
                PortraitFile = vm.Message.PortraitFile,
                Timestamp    = vm.Message.Timestamp,
            }).ToList()
        };

        ChatLogService.Save(key, state);
    }

    // ── Summarization ──────────────────────────────────────────────────────────

    private async Task TrySummarizeAsync(string key)
    {
        if (!_conversations.TryGetValue(key, out var vms)) return;
        if (vms.Count <= SummarizeThreshold) return;

        var toSummarize = vms.Take(vms.Count - KeepRecentCount).Select(vm => vm.Message).ToList();
        _memory.TryGetValue(key, out var existingMemory);

        try
        {
            StatusText = "Condensing earlier conversation…";
            var summary = await _openRouter.SummarizeAsync(toSummarize, existingMemory);
            if (string.IsNullOrWhiteSpace(summary)) return;

            _memory[key] = summary;

            for (int i = 0; i < toSummarize.Count; i++)
                vms.RemoveAt(0);

            vms.Insert(0, new ChatMessageVm(new ChatMessage
            {
                IsSummary  = true,
                SenderName = "Memory",
                Text       = summary,
                Timestamp  = DateTime.Now,
            }));

            AutoSave(key);
        }
        catch { /* summarization failure is non-fatal */ }
        finally
        {
            StatusText = "";
        }
    }

    private string? GetMemory(string key)
    {
        _memory.TryGetValue(key, out var m);
        return string.IsNullOrEmpty(m) ? null : m;
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
        OnPropertyChanged(nameof(CanRegenerate));
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
