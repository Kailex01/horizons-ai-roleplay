using System.Windows.Input;
using System.Windows.Media;
using HorizonsAI.Models;
using HorizonsAI.Services;
using Microsoft.Win32;

namespace HorizonsAI;

public partial class CharacterEditWindow : Window
{
    private readonly Character _character;
    private readonly bool      _isEdit;
    private string?            _pendingPortraitSource;

    // ── Voice row tracking ─────────────────────────────────────────────────────

    private class VoiceRow
    {
        public required Grid     Container;
        public required ComboBox VoiceBox;
        public required TextBox  WeightBox;
    }

    private readonly List<VoiceRow> _voiceRows = new();

    private const int MaxVoices = 5;


    // ── Constructor ────────────────────────────────────────────────────────────

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

        NameBox.Text         = _character.Name;
        CategoryBox.Text     = _character.Category;
        PromptBox.Text       = _character.SystemPrompt;
        ModelBox.Text        = _character.Model;
        EnabledBox.IsChecked = _character.Enabled;

        var st = _character.Stats;
        StrBox.Text = st.Str.ToString();
        DexBox.Text = st.Dex.ToString();
        ConBox.Text = st.Con.ToString();
        IntBox.Text = st.Int.ToString();
        WisBox.Text = st.Wis.ToString();
        ChaBox.Text = st.Cha.ToString();

        LoadVoiceProfile();
    }

    // ── Voice profile UI ───────────────────────────────────────────────────────

    private void LoadVoiceProfile()
    {
        var p = _character.VoiceProfile;

        foreach (var entry in p.Voices)
            AddVoiceRowControl(entry.Voice, entry.Weight);

        SpeedBox.Text  = p.Speed.ToString("F1");
        PitchBox.Text  = p.PitchSemitones.ToString("F1");
        TempoBox.Text  = p.Tempo.ToString("F1");
        VolumeBox.Text = p.Volume.ToString("F1");

        UpdateAddButtonState();
    }

    private void AddVoiceRowControl(string voice = "", float weight = 1.0f)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

        var voiceBox = new ComboBox
        {
            IsEditable      = true,
            Text            = voice,
            Background      = new SolidColorBrush(Color.FromRgb(0x11, 0x1E, 0x2A)),
            Foreground      = new SolidColorBrush(Color.FromRgb(0xD4, 0xC5, 0xA0)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x50)),
            BorderThickness = new Thickness(1),
            FontSize        = 12,
            Padding         = new Thickness(6, 4, 6, 4),
            ToolTip         = "Type any voice name or pick from the list",
        };
        foreach (var v in KokoroService.GetInstalledVoiceNames()) voiceBox.Items.Add(v);

        var weightBox = new TextBox
        {
            Text            = weight.ToString("F1"),
            Background      = new SolidColorBrush(Color.FromRgb(0x11, 0x1E, 0x2A)),
            Foreground      = new SolidColorBrush(Color.FromRgb(0xD4, 0xC5, 0xA0)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x50)),
            BorderThickness = new Thickness(1),
            FontSize        = 12,
            Padding         = new Thickness(6, 5, 6, 5),
            TextAlignment   = TextAlignment.Center,
            ToolTip         = "Blend weight (any positive number — auto-normalised)",
        };

        var removeBtn = new Button
        {
            Content         = "×",
            Background      = new SolidColorBrush(Color.FromRgb(0x3A, 0x1A, 0x1A)),
            Foreground      = new SolidColorBrush(Color.FromRgb(0x9A, 0x5A, 0x5A)),
            BorderThickness = new Thickness(0),
            FontSize        = 14,
            Cursor          = Cursors.Hand,
            ToolTip         = "Remove this voice",
        };

        var row = new VoiceRow { Container = grid, VoiceBox = voiceBox, WeightBox = weightBox };
        removeBtn.Click += (_, _) =>
        {
            _voiceRows.Remove(row);
            VoiceEntriesPanel.Children.Remove(grid);
            UpdateAddButtonState();
        };

        Grid.SetColumn(voiceBox,   0);
        Grid.SetColumn(weightBox,  2);
        Grid.SetColumn(removeBtn,  4);
        grid.Children.Add(voiceBox);
        grid.Children.Add(weightBox);
        grid.Children.Add(removeBtn);

        VoiceEntriesPanel.Children.Add(grid);
        _voiceRows.Add(row);
    }

    private void UpdateAddButtonState()
        => AddVoiceBtn.IsEnabled = _voiceRows.Count < MaxVoices;

    private void AddVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceRows.Count >= MaxVoices) return;
        AddVoiceRowControl();
        UpdateAddButtonState();
    }

    private void SaveVoiceProfile()
    {
        var p = _character.VoiceProfile;
        p.Voices.Clear();

        foreach (var row in _voiceRows)
        {
            var name = row.VoiceBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (!float.TryParse(row.WeightBox.Text, out var w)) w = 1.0f;
            p.Voices.Add(new VoiceWeight { Voice = name, Weight = Math.Max(0.01f, w) });
        }

        if (float.TryParse(SpeedBox.Text,  out var speed))  p.Speed          = Math.Clamp(speed,  0.5f, 2.0f);
        if (float.TryParse(PitchBox.Text,  out var pitch))  p.PitchSemitones = Math.Clamp(pitch, -12f, 12f);
        if (float.TryParse(TempoBox.Text,  out var tempo))  p.Tempo          = Math.Clamp(tempo,  0.5f, 2.0f);
        if (float.TryParse(VolumeBox.Text, out var volume)) p.Volume         = Math.Clamp(volume, 0.0f, 2.0f);
    }

    // ── Title bar ──────────────────────────────────────────────────────────────

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e) => DragMove();

    // ── Portrait ───────────────────────────────────────────────────────────────

    private void Portrait_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select Portrait Image",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All Files|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        _pendingPortraitSource = dlg.FileName;

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

    // ── Save / Delete / Cancel ─────────────────────────────────────────────────

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

        var st = _character.Stats;
        if (int.TryParse(StrBox.Text, out var str)) st.Str = Math.Clamp(str, 1, 30);
        if (int.TryParse(DexBox.Text, out var dex)) st.Dex = Math.Clamp(dex, 1, 30);
        if (int.TryParse(ConBox.Text, out var con)) st.Con = Math.Clamp(con, 1, 30);
        if (int.TryParse(IntBox.Text, out var int_)) st.Int = Math.Clamp(int_, 1, 30);
        if (int.TryParse(WisBox.Text, out var wis)) st.Wis = Math.Clamp(wis, 1, 30);
        if (int.TryParse(ChaBox.Text, out var cha)) st.Cha = Math.Clamp(cha, 1, 30);

        SaveVoiceProfile();

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
