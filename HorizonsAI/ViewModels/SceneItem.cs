using HorizonsAI.Models;

namespace HorizonsAI.ViewModels;

public class SceneItem : INotifyPropertyChanged
{
    public Scene  Scene       { get; }
    public string DisplayName => Scene.Name;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public SceneItem(Scene s) => Scene = s;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
