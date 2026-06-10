using GukChat.Models;

namespace GukChat.ViewModels;

public class CharacterItem : INotifyPropertyChanged
{
    public Character Character { get; }

    private BitmapImage? _portrait;
    public BitmapImage? Portrait
    {
        get => _portrait;
        set { _portrait = value; OnPropertyChanged(); }
    }

    public string DisplayName   => Character.DisplayName;
    public string CategoryBadge => Character.CategoryBadge;

    public CharacterItem(Character c) => Character = c;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
