using System.Windows.Input;
using HorizonsAI.Models;
using HorizonsAI.Services;

namespace HorizonsAI;

public partial class SceneEditWindow : Window
{
    private readonly Scene _scene;
    private readonly bool  _isEdit;

    public SceneEditWindow(Scene? existing = null)
    {
        InitializeComponent();
        _isEdit = existing != null;
        _scene  = existing != null
            ? JsonSerializer.Deserialize<Scene>(JsonSerializer.Serialize(existing))!
            : new Scene();

        if (_isEdit)
        {
            TitleText.Text       = "EDIT SCENE";
            DeleteBtn.Visibility = Visibility.Visible;
        }

        NameBox.Text    = _scene.Name;
        ContextBox.Text = _scene.Context;
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e) => DragMove();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Scene name is required.", "Horizon's AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _scene.Name    = name;
        _scene.Context = ContextBox.Text.Trim();
        SceneService.Save(_scene);
        DialogResult = true;
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"Delete scene \"{_scene.Name}\"? This cannot be undone.",
            "Horizon's AI", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        SceneService.Delete(_scene);
        DialogResult = false;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
