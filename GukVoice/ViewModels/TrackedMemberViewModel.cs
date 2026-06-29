namespace GukVoice.ViewModels;

// Represents one radio-button option in the GROUP MEMBERS section.
// IsActive is mutually exclusive — setting it true updates FctViewModel.ActiveSubject,
// which then notifies all siblings to re-evaluate their own IsActive.
public class GroupMemberRadioViewModel : INotifyPropertyChanged
{
    private readonly FctViewModel _parent;

    public GroupMemberRadioViewModel(string name, FctViewModel parent)
    {
        Name    = name;
        _parent = parent;
    }

    public string Name { get; }

    public bool IsActive
    {
        get => _parent.ActiveSubject.Equals(Name, StringComparison.OrdinalIgnoreCase);
        set { if (value) _parent.ActiveSubject = Name; }
    }

    public void NotifyIsActive() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));

    public event PropertyChangedEventHandler? PropertyChanged;
}
