using System.Text.RegularExpressions;
using GukVoice.Kokoro.Models;
using GukVoice.Kokoro.Services;
using HorizonsAI.Models;
using HorizonsAI.Services;

namespace HorizonsAI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly HttpClient        _http   = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly OpenRouterService _openRouter;
    private readonly KokoroService     _kokoro = new(AppConfig.TtsFolder);
    private readonly NarratorService   _narrator;
    private readonly Dictionary<string, ObservableCollection<ChatMessageVm>> _conversations = new();
    private readonly Dictionary<string, string>          _memory    = new();
    private readonly Dictionary<string, List<SceneNpc>>  _sceneNpcs       = new();
    private readonly Dictionary<string, int>            _sceneDc         = new();
    private readonly Dictionary<string, string>         _sceneDifficulty = new();
    private static readonly Random                      _rng             = new();
    private Lorebook _lorebook = new();
    private CancellationTokenSource _ttsCts       = new();
    private Task                     _npcVoiceTask = Task.CompletedTask;

    private const int SummarizeThreshold = 40;
    private const int KeepRecentCount    = 20;

    // ── Sidebar ────────────────────────────────────────────────────────────────

    public ObservableCollection<CategoryGroup> Categories       { get; } = new();
    public ObservableCollection<PartyItem>     Parties          { get; } = new();
    public ObservableCollection<SceneItem>     Scenes           { get; } = new();
    public ObservableCollection<SceneNpc>      CurrentSceneNpcs { get; } = new();
    public bool HasSceneNpcs => CurrentSceneNpcs.Count > 0;

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
                if (_selectedScene != null)
                {
                    _selectedScene.IsSelected = false;
                    _selectedScene = null;
                    OnPropertyChanged(nameof(SelectedScene));
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
                if (_selectedScene != null)
                {
                    _selectedScene.IsSelected = false;
                    _selectedScene = null;
                    OnPropertyChanged(nameof(SelectedScene));
                }
            }
            NotifyActiveChanged();
            SwitchConversation();
        }
    }

    public ICommand SelectPartyCommand { get; }

    // ── Selected scene ─────────────────────────────────────────────────────────

    private SceneItem? _selectedScene;
    public SceneItem? SelectedScene
    {
        get => _selectedScene;
        set
        {
            if (_selectedScene == value) return;
            if (_selectedScene != null) _selectedScene.IsSelected = false;
            _selectedScene = value;
            if (_selectedScene != null)
            {
                _selectedScene.IsSelected = true;
                foreach (var c in _allCharactersFlat.Where(c => c.IsSelected)) c.IsSelected = false;
                _selectedCharacter = null;
                OnPropertyChanged(nameof(SelectedCharacter));
                if (_selectedParty != null) { _selectedParty.IsSelected = false; _selectedParty = null; OnPropertyChanged(nameof(SelectedParty)); }
            }
            NotifyActiveChanged();
            SwitchConversation();
        }
    }

    public ICommand SelectSceneCommand { get; }

    // ── Active header properties ───────────────────────────────────────────────

    public BitmapImage? ActivePortrait
        => _selectedCharacter?.Portrait
           ?? _selectedParty?.Members.FirstOrDefault()?.Portrait;

    public string ActiveCharacterName
        => _selectedCharacter?.DisplayName
           ?? _selectedParty?.DisplayName
           ?? _selectedScene?.DisplayName
           ?? "Select a character, party, or scene";

    public string ActiveCategoryBadge
        => _selectedCharacter?.CategoryBadge
           ?? (_selectedParty != null ? "PARTY"
           : _selectedScene   != null ? "SCENE"
           : "");

    public bool HasActiveCharacter => _selectedCharacter != null || _selectedParty != null || _selectedScene != null;
    public bool IsPartyActive      => _selectedParty != null;
    public bool IsSceneActive      => _selectedScene != null;

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

    private readonly List<string> _inputHistory = new(3);
    public IReadOnlyList<string> InputHistory => _inputHistory;

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

    private string _tokenUsageText = "";
    public string TokenUsageText
    {
        get => _tokenUsageText;
        private set { _tokenUsageText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTokenUsage)); }
    }
    public bool HasTokenUsage => !string.IsNullOrEmpty(_tokenUsageText);

    private bool _isVoiceEnabled;
    public bool IsVoiceEnabled
    {
        get => _isVoiceEnabled;
        set
        {
            _isVoiceEnabled = value;
            OnPropertyChanged();
            if (value && !KokoroService.IsModelReady(AppConfig.TtsFolder))
                TtsSetupRequested?.Invoke();
        }
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
    public ICommand ReloadTtsCommand  { get; }

    // ── Events ─────────────────────────────────────────────────────────────────

    public event Action? ScrollToBottom;
    public event Action? TtsSetupRequested;

    // ── Constructor ────────────────────────────────────────────────────────────

    public MainViewModel()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("HorizonsAI/1.0");
        _openRouter  = new OpenRouterService(_http);
        _narrator    = new NarratorService(_http);
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

        SelectSceneCommand = new RelayCommand(
            p => { if (p is SceneItem item) SelectedScene = item; return Task.CompletedTask; });

        ReloadTtsCommand = new RelayCommand(_ => OnReloadTts());
    }

    private void OnReloadTts()
    {
        StatusText = "Reloading TTS…";
        try
        {
            var allProfiles = _allCharactersFlat
                .Select(c => c.Character.VoiceProfile)
                .Append(AppConfig.Current.NarratorVoiceProfile);
            _kokoro.Reinitialize(allProfiles);
            StatusText = _kokoro.BlendDiagnostic;
        }
        catch (Exception ex) { StatusText = $"TTS reload failed: {ex.Message}"; }
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
        _sceneNpcs.Remove(key);
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
                foreach (var c in _allCharactersFlat.Where(c => c.IsSelected)) c.IsSelected = false;
                _selectedCharacter = null;
                if (_selectedScene != null) { _selectedScene.IsSelected = false; _selectedScene = null; }
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
        _sceneNpcs.Remove(key);
        if (SelectedParty == item) SelectedParty = null;
        LoadParties();
    }

    // ── Scene management ───────────────────────────────────────────────────────

    public void LoadScenes()
    {
        var prevId = _selectedScene?.Scene.Id;
        Scenes.Clear();
        foreach (var s in SceneService.LoadAll())
            Scenes.Add(new SceneItem(s));

        if (prevId != null)
        {
            var match = Scenes.FirstOrDefault(s => s.Scene.Id == prevId);
            if (match != null)
            {
                match.IsSelected = true;
                _selectedScene   = match;
                foreach (var c in _allCharactersFlat.Where(c => c.IsSelected)) c.IsSelected = false;
                _selectedCharacter = null;
                if (_selectedParty != null) { _selectedParty.IsSelected = false; _selectedParty = null; }
                NotifyActiveChanged();
            }
        }
    }

    public void DeleteScene(SceneItem item)
    {
        var key = $"scene_{item.Scene.Id}";
        SceneService.Delete(item.Scene);
        ChatLogService.Delete(key);
        _conversations.Remove(key);
        _memory.Remove(key);
        _sceneNpcs.Remove(key);
        _sceneDc.Remove(key);
        _sceneDifficulty.Remove(key);
        if (SelectedScene == item) SelectedScene = null;
        LoadScenes();
    }

    public void PromoteSceneNpc(SceneNpc npc)
    {
        if (npc.CharacterId == null) return;
        var existing = _allCharactersFlat.FirstOrDefault(c => c.Character.Id == npc.CharacterId);
        if (existing == null) return;

        var oldPath = Path.Combine(AppConfig.CharactersFolder, existing.Character.Category, $"{existing.Character.Id}.json");
        existing.Character.Category = "npcs";
        CharacterService.Save(existing.Character);
        if (File.Exists(oldPath)) File.Delete(oldPath);

        LoadCharacters();
    }

    public void LoadLorebook() => _lorebook = LorebookService.Load();

    public void OnSettingsChanged()
    {
        _authorsNote = AppConfig.Current.AuthorsNote;
        OnPropertyChanged(nameof(AuthorsNote));
    }

    public void InitializeTts()
    {
        var allProfiles = _allCharactersFlat
            .Select(c => c.Character.VoiceProfile)
            .Append(AppConfig.Current.NarratorVoiceProfile);
        _kokoro.Initialize(allProfiles);
    }

    // ── Conversation management ────────────────────────────────────────────────

    private void SwitchConversation()
    {
        var key = ConversationKey();
        RefreshSceneRoster(key);
        if (key is null) return;

        if (!_conversations.ContainsKey(key))
            LoadConversationFromDisk(key);

        Messages   = _conversations[key];
        StatusText = "";
        ScrollToBottom?.Invoke();
    }

    private void RefreshSceneRoster(string? key)
    {
        CurrentSceneNpcs.Clear();
        if (key != null && _sceneNpcs.TryGetValue(key, out var npcs))
            foreach (var npc in npcs) CurrentSceneNpcs.Add(npc);
        OnPropertyChanged(nameof(HasSceneNpcs));
    }

    private void LoadConversationFromDisk(string key)
    {
        var state = ChatLogService.Load(key);
        _memory[key]    = state.Memory ?? "";
        _sceneNpcs[key] = new List<SceneNpc>(state.SceneNpcs);

        var loaded = new ObservableCollection<ChatMessageVm>();
        foreach (var dto in state.Messages)
        {
            loaded.Add(new ChatMessageVm(new ChatMessage
            {
                Text             = dto.Text,
                IsPlayer         = dto.IsPlayer,
                IsSummary        = dto.IsSummary,
                IsNarratorAction = dto.IsNarratorAction,
                SenderName       = dto.SenderName,
                PortraitFile     = dto.PortraitFile,
                Portrait         = !string.IsNullOrEmpty(dto.PortraitFile)
                                   ? PortraitService.Load(dto.PortraitFile) : null,
                Timestamp        = dto.Timestamp,
            }));
        }
        _conversations[key] = loaded;
    }

    private string? ConversationKey()
        => _selectedCharacter?.Character.Id
           ?? (_selectedParty  != null ? $"party_{_selectedParty.Party.Id}"   : null)
           ?? (_selectedScene  != null ? $"scene_{_selectedScene.Scene.Id}"   : null);

    // ── Send message ───────────────────────────────────────────────────────────

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var key = ConversationKey();
        if (key is null) return;

        var rawText = InputText.Trim();
        _inputHistory.RemoveAll(h => h == rawText);
        _inputHistory.Insert(0, rawText);
        if (_inputHistory.Count > 3) _inputHistory.RemoveAt(3);
        InputText = "";
        var text    = ResolveChecks(rawText, key);
        IsSending   = true;

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
            else if (_selectedScene != null)
                await SendToSceneAsync(_selectedScene, key, text);
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
            else if (_selectedScene != null)
                await SendToSceneAsync(_selectedScene, key, lastText);
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

        var sceneNpcs = _sceneNpcs.TryGetValue(key, out var sn) ? sn : new List<SceneNpc>();

        if (sceneNpcs.Count > 0)
        {
            // Scene has grown — promote to multi-NPC party-style call
            var members = new List<(string Name, string SystemPrompt)>
                { (charItem.DisplayName, charItem.Character.SystemPrompt ?? "") };
            members.AddRange(sceneNpcs.Select(n => (n.Name, n.Personality)));

            var portraitMap = new Dictionary<string, BitmapImage?> { [charItem.DisplayName] = charItem.Portrait };
            var fileMap     = new Dictionary<string, string?>       { [charItem.DisplayName] = charItem.Character.Portrait };
            var profileMap  = new Dictionary<string, VoiceProfile>  { [charItem.DisplayName] = charItem.Character.VoiceProfile };

            var memory  = GetMemory(key);
            var lore    = OpenRouterService.MatchLore(Messages.Select(vm => vm.Message), text, _lorebook.Entries);
            var replies = await _openRouter.ChatPartyAsync(
                "",
                members,
                Messages.SkipLast(1).Select(vm => vm.Message),
                text,
                _playAsCharacter?.Character,
                memory,
                lore,
                _authorsNote);

            replies = replies.Select(r => (r.Name, ResolveNpcChecks(r.Text, key, FindNpcCharacter(r.Name, key)))).ToList();

            int totalAdded = 0;
            foreach (var (name, msg) in replies)
            {
                portraitMap.TryGetValue(name, out var portrait);
                fileMap.TryGetValue(name, out var portraitFile);
                foreach (var (segText, isAction) in KokoroService.ParseSegments(msg))
                {
                    Messages.Add(new ChatMessageVm(new ChatMessage
                    {
                        Text             = segText,
                        IsPlayer         = false,
                        IsNarratorAction = isAction,
                        SenderName       = isAction ? "" : name,
                        Portrait         = isAction ? null : portrait,
                        PortraitFile     = isAction ? null : portraitFile,
                        Timestamp        = DateTime.Now,
                    }));
                    totalAdded++;
                }
            }
            ScrollToBottom?.Invoke();
            StatusText = "";
            if (_openRouter.LastUsage is { } up)
                TokenUsageText = $"prompt {up.PromptTokens:N0}  ·  reply {up.CompletionTokens:N0}  ·  total {up.TotalTokens:N0} tokens";
            OnPropertyChanged(nameof(CanRegenerate));

            if (IsVoiceEnabled && replies.Count > 0)
            {
                _ttsCts.Cancel();
                _ttsCts = new CancellationTokenSource();
                var ct = _ttsCts.Token;
                var synthMsgs = Messages.TakeLast(totalAdded).Where(m => !m.IsNarratorAction).ToList();
                foreach (var m in synthMsgs) m.IsSynthesizing = true;

                var voiceTask = Task.Run(async () =>
                {
                    var narratorFallback = AppConfig.Current.NarratorVoiceProfile;
                    foreach (var (name, msg) in replies)
                    {
                        if (ct.IsCancellationRequested) break;
                        VoiceProfile? profile = profileMap.TryGetValue(name, out var p) && p.IsEnabled ? p : null;
                        profile ??= narratorFallback.IsEnabled ? narratorFallback : null;
                        if (profile == null) continue;
                        await _kokoro.SpeakAsync(msg, profile, narratorFallback, ct).ConfigureAwait(false);
                    }
                }, ct);
                _npcVoiceTask = voiceTask;
                _ = voiceTask.ContinueWith(t =>
                {
                    var ex = t.Exception?.GetBaseException();
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var m in synthMsgs) m.IsSynthesizing = false;
                        if (t.IsFaulted)
                            StatusText = $"Voice error: {ex?.GetType().Name}: {ex?.Message} @ {ex?.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";
                    });
                }, TaskScheduler.Default);
            }
        }
        else
        {
            // Solo NPC path
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

            var rawText  = ResolveNpcChecks(string.Join(" ", lines), key, charItem.Character);
            var segments = KokoroService.ParseSegments(rawText).ToList();
            foreach (var (segText, isAction) in segments)
            {
                Messages.Add(new ChatMessageVm(new ChatMessage
                {
                    Text             = segText,
                    IsPlayer         = false,
                    IsNarratorAction = isAction,
                    SenderName       = isAction ? "" : charItem.DisplayName,
                    Portrait         = isAction ? null : charItem.Portrait,
                    PortraitFile     = isAction ? null : charItem.Character.Portrait,
                    Timestamp        = DateTime.Now,
                }));
            }
            ScrollToBottom?.Invoke();
            StatusText = "";
            if (_openRouter.LastUsage is { } u)
                TokenUsageText = $"prompt {u.PromptTokens:N0}  ·  reply {u.CompletionTokens:N0}  ·  total {u.TotalTokens:N0} tokens";
            OnPropertyChanged(nameof(CanRegenerate));

            if (IsVoiceEnabled)
            {
                if (!_kokoro.IsInitialized)
                    StatusText = "Voice: model not loaded — restart the app or re-download the model.";
                else if (!charItem.Character.VoiceProfile.IsEnabled)
                    StatusText = "Voice: no voice set for this character — edit the character and add a voice.";
                else
                {
                    var synthMsgs = Messages.TakeLast(segments.Count).Where(m => !m.IsNarratorAction).ToList();
                    foreach (var m in synthMsgs) m.IsSynthesizing = true;

                    _ttsCts.Cancel();
                    _ttsCts = new CancellationTokenSource();
                    var ct = _ttsCts.Token;
                    _ = _kokoro.SpeakAsync(rawText, charItem.Character.VoiceProfile,
                            AppConfig.Current.NarratorVoiceProfile, ct)
                        .ContinueWith(t =>
                        {
                            var ex = t.Exception?.GetBaseException();
                            Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                foreach (var m in synthMsgs) m.IsSynthesizing = false;
                                if (t.IsFaulted)
                                    StatusText = $"Voice error: {ex?.GetType().Name}: {ex?.Message} @ {ex?.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";
                            });
                        }, TaskScheduler.Default);
                }
            }
        }

        AutoSave(key);
        if (Messages.Count > SummarizeThreshold)
            await TrySummarizeAsync(key);
        _ = FireNarratorAsync(key);
    }

    private async Task SendToPartyAsync(PartyItem partyItem, string key, string text)
    {
        StatusText = $"{partyItem.DisplayName} is responding…";

        var memory = GetMemory(key);
        var lore   = OpenRouterService.MatchLore(Messages.Select(vm => vm.Message), text, _lorebook.Entries);

        // Merge catalog party members with any narrator-added scene NPCs
        var members = partyItem.Members
            .Select(m => (m.DisplayName, m.Character.SystemPrompt ?? ""))
            .ToList<(string Name, string SystemPrompt)>();
        if (_sceneNpcs.TryGetValue(key, out var sceneMbrs))
            members.AddRange(sceneMbrs.Select(n => (n.Name, n.Personality)));

        var replies = await _openRouter.ChatPartyAsync(
            partyItem.Party.Context ?? "",
            members,
            Messages.SkipLast(1).Select(vm => vm.Message),
            text,
            _playAsCharacter?.Character,
            memory,
            lore,
            _authorsNote);

        var portraitMap = partyItem.Members.ToDictionary(m => m.Character.Name, m => m.Portrait);
        var fileMap     = partyItem.Members.ToDictionary(m => m.Character.Name, m => m.Character.Portrait);
        var profileMap  = partyItem.Members.ToDictionary(m => m.Character.Name, m => m.Character.VoiceProfile);

        replies = replies.Select(r => (r.Name, ResolveNpcChecks(r.Text, key, FindNpcCharacter(r.Name, key)))).ToList();

        int totalAdded = 0;
        foreach (var (name, msg) in replies)
        {
            portraitMap.TryGetValue(name, out var portrait);
            fileMap.TryGetValue(name, out var portraitFile);
            foreach (var (segText, isAction) in KokoroService.ParseSegments(msg))
            {
                Messages.Add(new ChatMessageVm(new ChatMessage
                {
                    Text             = segText,
                    IsPlayer         = false,
                    IsNarratorAction = isAction,
                    SenderName       = isAction ? "" : name,
                    Portrait         = isAction ? null : portrait,
                    PortraitFile     = isAction ? null : portraitFile,
                    Timestamp        = DateTime.Now,
                }));
                totalAdded++;
            }
        }
        ScrollToBottom?.Invoke();
        StatusText = "";
        if (_openRouter.LastUsage is { } up)
            TokenUsageText = $"prompt {up.PromptTokens:N0}  ·  reply {up.CompletionTokens:N0}  ·  total {up.TotalTokens:N0} tokens";
        OnPropertyChanged(nameof(CanRegenerate));

        if (IsVoiceEnabled && replies.Count > 0)
        {
            _ttsCts.Cancel();
            _ttsCts = new CancellationTokenSource();
            var ct = _ttsCts.Token;
            var synthMsgs = Messages.TakeLast(totalAdded).Where(m => !m.IsNarratorAction).ToList();
            foreach (var m in synthMsgs) m.IsSynthesizing = true;

            var partyVoiceTask = Task.Run(async () =>
            {
                var narratorFallback = AppConfig.Current.NarratorVoiceProfile;
                foreach (var (name, msg) in replies)
                {
                    if (ct.IsCancellationRequested) break;
                    VoiceProfile? profile = profileMap.TryGetValue(name, out var p) && p.IsEnabled ? p : null;
                    profile ??= narratorFallback.IsEnabled ? narratorFallback : null;
                    if (profile == null) continue;
                    await _kokoro.SpeakAsync(msg, profile, narratorFallback, ct).ConfigureAwait(false);
                }
            }, ct);
            _npcVoiceTask = partyVoiceTask;
            _ = partyVoiceTask.ContinueWith(t =>
            {
                var ex = t.Exception?.GetBaseException();
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var m in synthMsgs) m.IsSynthesizing = false;
                    if (t.IsFaulted)
                        StatusText = $"Voice error: {ex?.GetType().Name}: {ex?.Message} @ {ex?.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";
                });
            }, TaskScheduler.Default);
        }

        AutoSave(key);
        if (Messages.Count > SummarizeThreshold)
            await TrySummarizeAsync(key);
        _ = FireNarratorAsync(key);
    }

    private async Task SendToSceneAsync(SceneItem sceneItem, string key, string text)
    {
        StatusText = "Scene is responding…";

        if (!_sceneNpcs.TryGetValue(key, out var sceneMembers) || sceneMembers.Count == 0)
        {
            StatusText = "";
            AutoSave(key);
            _ = FireNarratorAsync(key, forceEnabled: true);
            return;
        }

        var memory  = GetMemory(key);
        var lore    = OpenRouterService.MatchLore(Messages.Select(vm => vm.Message), text, _lorebook.Entries);
        var members = sceneMembers.Select(n => (n.Name, n.Personality)).ToList<(string Name, string SystemPrompt)>();

        var replies = await _openRouter.ChatPartyAsync(
            sceneItem.Scene.Context ?? "",
            members,
            Messages.SkipLast(1).Select(vm => vm.Message),
            text,
            _playAsCharacter?.Character,
            memory,
            lore,
            _authorsNote);

        var profileMap = new Dictionary<string, VoiceProfile>();
        foreach (var npc in sceneMembers)
        {
            if (npc.CharacterId != null)
            {
                var charItem = _allCharactersFlat.FirstOrDefault(c => c.Character.Id == npc.CharacterId);
                if (charItem != null) profileMap[npc.Name] = charItem.Character.VoiceProfile;
            }
        }

        replies = replies.Select(r => (r.Name, ResolveNpcChecks(r.Text, key, FindNpcCharacter(r.Name, key)))).ToList();

        int totalAdded = 0;
        foreach (var (name, msg) in replies)
        {
            foreach (var (segText, isAction) in KokoroService.ParseSegments(msg))
            {
                Messages.Add(new ChatMessageVm(new ChatMessage
                {
                    Text             = segText,
                    IsPlayer         = false,
                    IsNarratorAction = isAction,
                    SenderName       = isAction ? "" : name,
                    Timestamp        = DateTime.Now,
                }));
                totalAdded++;
            }
        }
        ScrollToBottom?.Invoke();
        StatusText = "";
        if (_openRouter.LastUsage is { } up)
            TokenUsageText = $"prompt {up.PromptTokens:N0}  ·  reply {up.CompletionTokens:N0}  ·  total {up.TotalTokens:N0} tokens";
        OnPropertyChanged(nameof(CanRegenerate));

        if (IsVoiceEnabled && replies.Count > 0)
        {
            _ttsCts.Cancel();
            _ttsCts = new CancellationTokenSource();
            var ct = _ttsCts.Token;
            var synthMsgs = Messages.TakeLast(totalAdded).Where(m => !m.IsNarratorAction).ToList();
            foreach (var m in synthMsgs) m.IsSynthesizing = true;

            var voiceTask = Task.Run(async () =>
            {
                var narratorFallback = AppConfig.Current.NarratorVoiceProfile;
                foreach (var (name, msg) in replies)
                {
                    if (ct.IsCancellationRequested) break;
                    VoiceProfile? profile = profileMap.TryGetValue(name, out var p) && p.IsEnabled ? p : null;
                    profile ??= narratorFallback.IsEnabled ? narratorFallback : null;
                    if (profile == null) continue;
                    await _kokoro.SpeakAsync(msg, profile, narratorFallback, ct).ConfigureAwait(false);
                }
            }, ct);
            _npcVoiceTask = voiceTask;
            _ = voiceTask.ContinueWith(t =>
            {
                var ex = t.Exception?.GetBaseException();
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var m in synthMsgs) m.IsSynthesizing = false;
                    if (t.IsFaulted)
                        StatusText = $"Voice error: {ex?.GetType().Name}: {ex?.Message} @ {ex?.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";
                });
            }, TaskScheduler.Default);
        }

        AutoSave(key);
        if (Messages.Count > SummarizeThreshold)
            await TrySummarizeAsync(key);
        _ = FireNarratorAsync(key, forceEnabled: true);
    }

    // ── Skill checks ───────────────────────────────────────────────────────────

    private string ResolveChecks(string text, string key)
    {
        // Skill checks — roll vs narrator DC; optional bonus e.g. [Check Str +2]
        text = Regex.Replace(text, @"\[Check\s+(Str|Dex|Con|Int|Wis|Cha)(?:\s+([+-]\d+))?\]",
            m =>
            {
                var stat        = m.Groups[1].Value;
                var bonus       = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                var dc          = _sceneDc.TryGetValue(key, out var d) ? d : 12;
                var diff        = _sceneDifficulty.TryGetValue(key, out var df) ? df.ToLower() : "normal";
                var effectiveDc = diff switch { "easy" => dc - 2, "hard" => dc + 2, _ => dc };
                var mod         = (_playAsCharacter?.Character.Stats.GetMod(stat) ?? 0) + bonus;
                var roll        = _rng.Next(1, 21);
                var total       = roll + mod;
                var modStr      = mod >= 0 ? $"+{mod}" : $"{mod}";
                var result      = total >= effectiveDc ? "SUCCESS" : "FAIL";
                return $"[{stat} Check: {roll}{modStr}={total} vs DC{effectiveDc} — {result}]";
            }, RegexOptions.IgnoreCase);

        // Attack rolls — roll vs target AC; optional bonus and/or "simple" keyword (any order)
        // e.g. [Attack Str], [Attack Str +2], [Attack Str simple], [Attack Str +2 simple]
        text = Regex.Replace(text, @"\[Attack\s+(Str|Dex|Con|Int|Wis|Cha)((?:\s+(?:[+-]\d+|simple))*)\]",
            m =>
            {
                var stat     = m.Groups[1].Value;
                var extras   = m.Groups[2].Value;
                var isSimple = Regex.IsMatch(extras, @"\bsimple\b", RegexOptions.IgnoreCase);
                var bonusM   = Regex.Match(extras, @"[+-]\d+");
                var bonus    = bonusM.Success ? int.Parse(bonusM.Value) : 0;
                var dieSides = isSimple ? 4 : 6;
                var (ac, targetName) = ResolveAttackTarget(text, m.Index, key);
                var mod    = (_playAsCharacter?.Character.Stats.GetMod(stat) ?? 0) + bonus;
                var roll   = _rng.Next(1, 21);
                var total  = roll + mod;
                var modStr = mod >= 0 ? $"+{mod}" : $"{mod}";
                var label  = targetName != null ? $" ({targetName})" : "";
                if (total >= ac)
                {
                    var dmg = _rng.Next(1, dieSides + 1);
                    return $"[{stat} Attack: {roll}{modStr}={total} vs AC{ac}{label} — HIT! 1d{dieSides}={dmg} dmg]";
                }
                return $"[{stat} Attack: {roll}{modStr}={total} vs AC{ac}{label} — MISS]";
            }, RegexOptions.IgnoreCase);

        return text;
    }

    // Resolves [check]/[attack] tokens in NPC response text using the NPC's own stats
    private string ResolveNpcChecks(string text, string key, Character? npc)
    {
        text = Regex.Replace(text, @"\[Check\s+(Str|Dex|Con|Int|Wis|Cha)(?:\s+([+-]\d+))?\]",
            m =>
            {
                var stat        = m.Groups[1].Value;
                var bonus       = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                var dc          = _sceneDc.TryGetValue(key, out var d) ? d : 12;
                var diff        = _sceneDifficulty.TryGetValue(key, out var df) ? df.ToLower() : "normal";
                var effectiveDc = diff switch { "easy" => dc - 2, "hard" => dc + 2, _ => dc };
                var mod         = (npc?.Stats.GetMod(stat) ?? 0) + bonus;
                var roll        = _rng.Next(1, 21);
                var total       = roll + mod;
                var modStr      = mod >= 0 ? $"+{mod}" : $"{mod}";
                var result      = total >= effectiveDc ? "SUCCESS" : "FAIL";
                return $"[{stat} Check: {roll}{modStr}={total} vs DC{effectiveDc} — {result}]";
            }, RegexOptions.IgnoreCase);

        text = Regex.Replace(text, @"\[Attack\s+(Str|Dex|Con|Int|Wis|Cha)((?:\s+(?:[+-]\d+|simple))*)\]",
            m =>
            {
                var stat     = m.Groups[1].Value;
                var extras   = m.Groups[2].Value;
                var isSimple = Regex.IsMatch(extras, @"\bsimple\b", RegexOptions.IgnoreCase);
                var bonusM   = Regex.Match(extras, @"[+-]\d+");
                var bonus    = bonusM.Success ? int.Parse(bonusM.Value) : 0;
                var dieSides = isSimple ? 4 : 6;
                var mod      = (npc?.Stats.GetMod(stat) ?? 0) + bonus;
                var playerAc = _playAsCharacter?.Character.Stats.Ac ?? 10;
                var target   = _playAsCharacter?.Character.Name ?? "player";
                var roll     = _rng.Next(1, 21);
                var total    = roll + mod;
                var modStr   = mod >= 0 ? $"+{mod}" : $"{mod}";
                if (total >= playerAc)
                {
                    var dmg = _rng.Next(1, dieSides + 1);
                    return $"[{stat} Attack: {roll}{modStr}={total} vs AC{playerAc} ({target}) — HIT! 1d{dieSides}={dmg} dmg]";
                }
                return $"[{stat} Attack: {roll}{modStr}={total} vs AC{playerAc} ({target}) — MISS]";
            }, RegexOptions.IgnoreCase);

        return text;
    }

    private Character? FindNpcCharacter(string name, string key)
    {
        var item = _allCharactersFlat.FirstOrDefault(c =>
            c.Character.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (item != null) return item.Character;

        if (_sceneNpcs.TryGetValue(key, out var npcs))
        {
            var npc = npcs.FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (npc?.CharacterId != null)
            {
                var c = _allCharactersFlat.FirstOrDefault(x => x.Character.Id == npc.CharacterId);
                if (c != null) return c.Character;
            }
        }
        return null;
    }

    private (int Ac, string? Name) ResolveAttackTarget(string text, int tokenIndex, string key)
    {
        if (_sceneNpcs.TryGetValue(key, out var npcs) && npcs.Count > 0)
        {
            // Search ±100 chars around the token for a named NPC
            int start  = Math.Max(0, tokenIndex - 100);
            int len    = Math.Min(text.Length - start, 200);
            var window = text.Substring(start, len);
            foreach (var npc in npcs)
            {
                if (window.Contains(npc.Name, StringComparison.OrdinalIgnoreCase))
                    return (NpcAc(npc), npc.Name);
            }
            return (NpcAc(npcs[0]), npcs[0].Name);
        }
        // No scene NPCs — fall back to narrator DC
        var dc = _sceneDc.TryGetValue(key, out var d) ? d : 12;
        return (dc, null);
    }

    private int NpcAc(SceneNpc npc)
    {
        if (npc.CharacterId != null)
        {
            var c = _allCharactersFlat.FirstOrDefault(x => x.Character.Id == npc.CharacterId);
            if (c != null) return c.Character.Stats.Ac;
        }
        return 10;
    }

    // ── Narrator / GM ──────────────────────────────────────────────────────────

    private async Task FireNarratorAsync(string key, bool forceEnabled = false)
    {
        // Capture synchronously before any awaits — prevents mid-flight conversation-switch races
        if (!_conversations.TryGetValue(key, out var convoVms)) return;
        var npcNames      = GetActiveNpcNames(key);
        var historySnap   = convoVms.Select(vm => vm.Message).ToList();
        var ct            = _ttsCts.Token;
        var npcVoiceSnap  = _npcVoiceTask;

        var result = await _narrator.EvaluateAsync(historySnap, npcNames, forceEnabled);
        if (result == null) return;

        if (result.Dc.HasValue)        _sceneDc[key]         = result.Dc.Value;
        if (result.Difficulty != null) _sceneDifficulty[key] = result.Difficulty;

        // For each newly introduced NPC, generate a full character system prompt and save
        bool anyNewCharacters = false;
        if (result.Add.Count > 0)
        {
            var existingIds = CharacterService.LoadAll().Select(c => c.Id).ToHashSet();
            foreach (var sceneNpc in result.Add)
            {
                var id = Character.MakeId(sceneNpc.Name);
                if (existingIds.Contains(id))
                {
                    sceneNpc.CharacterId = id;
                    continue;
                }

                var draft = await _narrator.GenerateCharacterPromptAsync(
                    sceneNpc.Name, sceneNpc.Personality, historySnap);
                if (draft != null)
                {
                    CharacterService.Save(new Character
                    {
                        Id           = id,
                        Name         = sceneNpc.Name,
                        Category     = "scene_npcs",
                        Enabled      = true,
                        SystemPrompt = draft.SystemPrompt,
                        Stats        = draft.Stats,
                    });
                    sceneNpc.Personality  = draft.SystemPrompt;
                    sceneNpc.CharacterId  = id;
                    anyNewCharacters      = true;
                }
            }
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!_conversations.TryGetValue(key, out var vms)) return;

            if (result.Narration != null)
            {
                vms.Add(new ChatMessageVm(new ChatMessage
                {
                    Text             = result.Narration,
                    IsNarratorAction = true,
                    SenderName       = "",
                    Timestamp        = DateTime.Now,
                }));
                if (vms == Messages) ScrollToBottom?.Invoke();
            }

            if (!_sceneNpcs.ContainsKey(key))
                _sceneNpcs[key] = new List<SceneNpc>();

            _sceneNpcs[key].AddRange(result.Add);

            // Only remove from narrator-created NPCs, never from anchor characters
            foreach (var name in result.Remove)
                _sceneNpcs[key].RemoveAll(n =>
                    string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

            RefreshSceneRoster(key);

            if (anyNewCharacters) LoadCharacters();

            AutoSave(key);
        });

        // Narrator TTS — wait for all NPC voices to finish first, then speak
        if (result.Narration != null && IsVoiceEnabled)
        {
            var narratorProfile = AppConfig.Current.NarratorVoiceProfile;
            if (narratorProfile.IsEnabled)
            {
                try { await npcVoiceSnap.ConfigureAwait(false); } catch { }
                _ = _kokoro.SpeakAsync(result.Narration, narratorProfile, narratorProfile, ct);
            }
        }
    }

    private List<string> GetActiveNpcNames(string key)
    {
        var names = new List<string>();
        if (_selectedCharacter != null)
            names.Add(_selectedCharacter.DisplayName);
        else if (_selectedParty != null)
            names.AddRange(_selectedParty.Members.Select(m => m.DisplayName));
        if (_sceneNpcs.TryGetValue(key, out var npcs))
            names.AddRange(npcs.Select(n => n.Name));
        return names;
    }

    // ── Persistence ────────────────────────────────────────────────────────────

    private void AutoSave(string key)
    {
        if (!_conversations.TryGetValue(key, out var vms)) return;
        _memory.TryGetValue(key, out var memory);

        var state = new ConversationState
        {
            Memory    = string.IsNullOrEmpty(memory) ? null : memory,
            SceneNpcs = _sceneNpcs.TryGetValue(key, out var npcs) ? npcs : new(),
            Messages  = vms.Select(vm => new ChatMessageDto
            {
                Text             = vm.Message.Text,
                IsPlayer         = vm.Message.IsPlayer,
                IsSummary        = vm.Message.IsSummary,
                IsNarratorAction = vm.Message.IsNarratorAction,
                SenderName       = vm.Message.SenderName,
                PortraitFile     = vm.Message.PortraitFile,
                Timestamp        = vm.Message.Timestamp,
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
        OnPropertyChanged(nameof(IsSceneActive));
        OnPropertyChanged(nameof(ActivePartyMembers));
        OnPropertyChanged(nameof(CanRegenerate));
    }

    // ── Dispose ────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _ttsCts.Cancel();
        _kokoro.Dispose();
        _http.Dispose();
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
