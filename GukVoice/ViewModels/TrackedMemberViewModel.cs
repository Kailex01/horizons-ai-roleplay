using GukVoice.Models;

namespace GukVoice.ViewModels;

public class TrackedMemberViewModel : INotifyPropertyChanged
{
    private readonly TrackedMember _model;
    private readonly Action _save;

    public TrackedMemberViewModel(TrackedMember model, Action save)
    {
        _model = model;
        _save  = save;
    }

    public string Name => _model.Name;

    public bool Enabled
    {
        get => _model.Enabled;
        set { _model.Enabled = value; _save(); OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
