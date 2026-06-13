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
        public required Grid      Container;
        public required ComboBox  VoiceBox;
        public required Slider    WeightSlider;
        public required TextBlock WeightLabel;
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
        HpBox.Text  = st.Hp.ToString();
        AcBox.Text  = st.Ac.ToString();

        LoadVoiceProfile();
    }

    // ── Voice profile UI ───────────────────────────────────────────────────────

    private void LoadVoiceProfile()
    {
        var p = _character.VoiceProfile;

        foreach (var entry in p.Voices)
            AddVoiceRowControl(entry.Voice, entry.Weight);

        SpeedSlider.Value  = Math.Clamp(p.Speed,          0.5, 2.0);
        PitchSlider.Value  = Math.Clamp(p.PitchSemitones, -12.0, 12.0);
        TempoSlider.Value  = Math.Clamp(p.Tempo,          0.5, 2.0);
        VolumeSlider.Value = Math.Clamp(p.Volume,         0.0, 2.0);

        SpeedLabel.Text  = p.Speed.ToString("F2");
        PitchLabel.Text  = p.PitchSemitones.ToString("F1");
        TempoLabel.Text  = p.Tempo.ToString("F2");
        VolumeLabel.Text = p.Volume.ToString("F2");

        UpdateAddButtonState();
    }

    private void AddVoiceRowControl(string voice = "", float weight = 1.0f)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

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

        var weightLabel = new TextBlock
        {
            Text              = weight.ToString("F1"),
            Foreground        = new SolidColorBrush(Color.FromRgb(0xC8, 0xA0, 0x20)),
            FontSize          = 10,
            TextAlignment     = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var weightSlider = new Slider
        {
            Minimum             = 0.1,
            Maximum             = 5.0,
            Value               = Math.Clamp(weight, 0.1f, 5.0f),
            SmallChange         = 0.1,
            LargeChange         = 0.5,
            TickFrequency       = 0.1,
            IsSnapToTickEnabled = true,
            VerticalAlignment   = VerticalAlignment.Center,
            Style               = (Style)FindResource("DarkSlider"),
            ToolTip             = "Blend weight — higher = picked more often",
        };
        weightSlider.ValueChanged += (_, e) => weightLabel.Text = e.NewValue.ToString("F1");

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

        var row = new VoiceRow { Container = grid, VoiceBox = voiceBox, WeightSlider = weightSlider, WeightLabel = weightLabel };
        removeBtn.Click += (_, _) =>
        {
            _voiceRows.Remove(row);
            VoiceEntriesPanel.Children.Remove(grid);
            UpdateAddButtonState();
        };

        Grid.SetColumn(voiceBox,     0);
        Grid.SetColumn(weightLabel,  2);
        Grid.SetColumn(weightSlider, 4);
        Grid.SetColumn(removeBtn,    6);
        grid.Children.Add(voiceBox);
        grid.Children.Add(weightLabel);
        grid.Children.Add(weightSlider);
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
            p.Voices.Add(new VoiceWeight { Voice = name, Weight = Math.Max(0.01f, (float)row.WeightSlider.Value) });
        }

        p.Speed          = (float)Math.Clamp(SpeedSlider.Value,  0.5, 2.0);
        p.PitchSemitones = (float)Math.Clamp(PitchSlider.Value, -12.0, 12.0);
        p.Tempo          = (float)Math.Clamp(TempoSlider.Value,  0.5, 2.0);
        p.Volume         = (float)Math.Clamp(VolumeSlider.Value, 0.0, 2.0);
    }

    // ── Title bar ──────────────────────────────────────────────────────────────

    private void OnVoiceSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender == SpeedSlider  && SpeedLabel  != null) SpeedLabel.Text  = e.NewValue.ToString("F2");
        if (sender == PitchSlider  && PitchLabel  != null) PitchLabel.Text  = e.NewValue.ToString("F1");
        if (sender == TempoSlider  && TempoLabel  != null) TempoLabel.Text  = e.NewValue.ToString("F2");
        if (sender == VolumeSlider && VolumeLabel != null) VolumeLabel.Text = e.NewValue.ToString("F2");
    }

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
        if (int.TryParse(StrBox.Text, out var str))  st.Str = Math.Clamp(str,  1, 30);
        if (int.TryParse(DexBox.Text, out var dex))  st.Dex = Math.Clamp(dex,  1, 30);
        if (int.TryParse(ConBox.Text, out var con))  st.Con = Math.Clamp(con,  1, 30);
        if (int.TryParse(IntBox.Text, out var int_)) st.Int = Math.Clamp(int_, 1, 30);
        if (int.TryParse(WisBox.Text, out var wis))  st.Wis = Math.Clamp(wis,  1, 30);
        if (int.TryParse(ChaBox.Text, out var cha))  st.Cha = Math.Clamp(cha,  1, 30);
        if (int.TryParse(HpBox.Text,  out var hp))   st.Hp  = Math.Max(1, hp);
        if (int.TryParse(AcBox.Text,  out var ac))   st.Ac  = Math.Clamp(ac,  1, 30);

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
