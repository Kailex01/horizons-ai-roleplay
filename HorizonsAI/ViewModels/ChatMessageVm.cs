using HorizonsAI.Models;

namespace HorizonsAI.ViewModels;

public class ChatMessageVm : INotifyPropertyChanged
{
    private ChatMessage _msg;
    public ChatMessage Message => _msg;

    // Pass-through display properties
    public bool         IsPlayer     => _msg.IsPlayer;
    public bool         IsCharacter  => _msg.IsCharacter;
    public bool         IsSummary    => _msg.IsSummary;
    public string       SenderName   => _msg.SenderName;
    public string?      PortraitFile => _msg.PortraitFile;
    public BitmapImage? Portrait     => _msg.Portrait;
    public bool         HasPortrait  => _msg.HasPortrait;
    public string       TimeStr      => _msg.TimeStr;
    public DateTime     Timestamp    => _msg.Timestamp;

    // Text shows draft while editing, otherwise real text
    public string Text => _isEditing ? _editDraft : _msg.Text;

    // ── Edit state ─────────────────────────────────────────────────────────────

    private bool   _isEditing;
    private string _editDraft = "";

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            _isEditing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotEditing));
            OnPropertyChanged(nameof(Text));
        }
    }
    public bool IsNotEditing => !_isEditing;

    public string EditDraft
    {
        get => _editDraft;
        set { _editDraft = value; OnPropertyChanged(); }
    }

    public void BeginEdit()
    {
        EditDraft = _msg.Text;
        IsEditing = true;
    }

    public void CancelEdit() => IsEditing = false;

    public void CommitEdit()
    {
        var trimmed = _editDraft.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        _msg = new ChatMessage
        {
            Text         = trimmed,
            IsPlayer     = _msg.IsPlayer,
            IsSummary    = _msg.IsSummary,
            SenderName   = _msg.SenderName,
            PortraitFile = _msg.PortraitFile,
            Portrait     = _msg.Portrait,
            Timestamp    = _msg.Timestamp,
        };
        IsEditing = false;
        OnPropertyChanged(nameof(Text));
    }

    public ChatMessageVm(ChatMessage m) { _msg = m; _editDraft = m.Text; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
