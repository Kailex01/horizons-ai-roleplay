using System.Windows.Forms;
using System.Windows.Input;
using HorizonsAI.Models;
using HorizonsAI.Services;

namespace HorizonsAI;

public partial class CharacterEditWindow : Window
{
    private readonly Character _character;
    private readonly bool      _isEdit;
    private string?            _pendingPortraitSource;

    public CharacterEditWindow(Character? existing = null)
    {
        InitializeComponent();
        _isEdit    = existing != null;
        _character = existing != null
            ? JsonSerializer.Deserialize<Character>(JsonSerializer.Serialize(existing))!
            : new Character { Category = "npcs", Enabled = true };

        if (_isEdit)
        {
            TitleText.Text       = "EDIT CHARACTER";
            DeleteBtn.Visibility = Visibility.Visible;
        }

        NameBox.Text       = _character.Name;
        CategoryBox.Text   = _character.Category;
        PromptBox.Text     = _character.SystemPrompt;
        ModelBox.Text      = _character.Model;
        EnabledBox.IsChecked = _character.Enabled;

        if (_character.Portrait != null)
        {
            var img = PortraitService.Load(_character.Portrait);
            if (img != null) PortraitPreview.Source = img;
        }
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e) => DragMove();

    private void Portrait_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select Portrait Image",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All Files|*.*",
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _pendingPortraitSource = dlg.FileName;

        // Show preview using raw file (not yet saved)
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource   = new Uri(dlg.FileName);
            bmp.EndInit();
            PortraitPreview.Source = bmp;
        }
        catch { }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Name is required.", "Horizon's AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _character.Name         = name;
        _character.Id           = Character.MakeId(name);
        _character.Category     = (CategoryBox.Text ?? "npcs").Trim().ToLowerInvariant().Replace(' ', '_');
        _character.SystemPrompt = PromptBox.Text.Trim();
        _character.Model        = ModelBox.Text.Trim();
        _character.Enabled      = EnabledBox.IsChecked ?? true;

        // Import portrait if one was picked
        if (_pendingPortraitSource != null)
        {
            var fileName = PortraitService.Import(_pendingPortraitSource, _character.Id);
            if (fileName != null) _character.Portrait = fileName;
        }

        CharacterService.Save(_character);
        DialogResult = true;
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"Delete {_character.Name}? This cannot be undone.",
            "Horizon's AI", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        CharacterService.Delete(_character);
        DialogResult = false;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
