using GukVoice.Models;
using System.Windows.Controls;
using System.Windows.Media;

namespace GukVoice;

public partial class SpeakerEditWindow : Window
{
    private readonly SpeakerProfile _profile;
    private readonly bool           _isNew;
    private bool                    _adjustingWeights;

    private const int MaxVoices = 5;

    private class VoiceRow
    {
        public required Grid      Container;
        public required ComboBox  VoiceBox;
        public required Slider    WeightSlider;
        public required TextBlock WeightLabel;
    }

    private readonly List<VoiceRow> _rows = new();
    private readonly string[]       _voiceNames;

    public SpeakerEditWindow(SpeakerProfile profile, bool isNew)
    {
        _profile    = profile;
        _isNew      = isNew;
        _voiceNames = KokoroService.GetInstalledVoiceNames(AppConfig.TtsFolder);
        InitializeComponent();
        Title = isNew ? "Add Speaker" : $"Edit: {profile.Name}";
        Load();
    }

    // ── Load existing data ─────────────────────────────────────────────────────

    private void Load()
    {
        NameBox.Text    = _profile.Name;
        TypeBox.SelectedIndex = (int)_profile.Type;
        EnabledBox.IsChecked  = _profile.Enabled;
        SpeedSlider.Value  = Math.Clamp(_profile.VoiceProfile.Speed,          0.5, 2.0);
        PitchSlider.Value  = Math.Clamp(_profile.VoiceProfile.PitchSemitones, -12, 12);
        VolumeSlider.Value = Math.Clamp(_profile.VoiceProfile.Volume,         0.1, 2.0);

        foreach (var vw in _profile.VoiceProfile.Voices)
            AddVoiceRow(vw.Voice, vw.Weight * 100f);

        if (_rows.Count == 0) AddVoiceRow("", 100f);
        UpdateAddButton();
    }

    // ── Voice rows ─────────────────────────────────────────────────────────────

    private void AddVoiceRow(string voice, float weightPct)
    {
        if (_rows.Count >= MaxVoices) return;

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

        // Voice dropdown
        var voiceBox = new ComboBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x42)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x58)),
            BorderThickness = new Thickness(1),
        };
        foreach (var v in _voiceNames) voiceBox.Items.Add(v);
        voiceBox.Text = voice;
        Grid.SetColumn(voiceBox, 0);

        // Weight slider
        var weightSlider = new Slider
        {
            Minimum = 1, Maximum = 100, Value = Math.Clamp(weightPct, 1, 100),
            TickFrequency = 1, IsSnapToTickEnabled = true,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xBF)),
        };
        Grid.SetColumn(weightSlider, 2);

        // Weight label
        var weightLabel = new TextBlock
        {
            Text       = $"{(int)weightPct}%",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xF0)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(weightLabel, 4);

        // Remove button
        var removeBtn = new Button
        {
            Content     = "✕",
            Background  = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
            Foreground  = new SolidColorBrush(Color.FromRgb(0xDD, 0x44, 0x44)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x8A)),
            BorderThickness = new Thickness(1),
            Padding     = new Thickness(0),
            Width = 24, Height = 24,
        };
        Grid.SetColumn(removeBtn, 6);

        var row = new VoiceRow { Container = grid, VoiceBox = voiceBox,
                                  WeightSlider = weightSlider, WeightLabel = weightLabel };

        weightSlider.ValueChanged += (_, _) => OnWeightChanged(row);
        removeBtn.Click           += (_, _) => RemoveVoiceRow(row);

        grid.Children.Add(voiceBox);
        grid.Children.Add(weightSlider);
        grid.Children.Add(weightLabel);
        grid.Children.Add(removeBtn);

        _rows.Add(row);
        VoiceRowsPanel.Children.Add(grid);
        UpdateAddButton();
    }

    private void RemoveVoiceRow(VoiceRow row)
    {
        _rows.Remove(row);
        VoiceRowsPanel.Children.Remove(row.Container);
        NormalizeWeights();
        UpdateAddButton();
    }

    private void OnWeightChanged(VoiceRow changed)
    {
        if (_adjustingWeights) return;
        changed.WeightLabel.Text = $"{(int)changed.WeightSlider.Value}%";
        NormalizeWeights();
    }

    private void NormalizeWeights()
    {
        if (_rows.Count == 0) return;
        var total = _rows.Sum(r => r.WeightSlider.Value);
        if (total <= 0) return;

        _adjustingWeights = true;
        foreach (var r in _rows)
            r.WeightLabel.Text = $"{(int)Math.Round(r.WeightSlider.Value / total * 100)}%";
        _adjustingWeights = false;
    }

    private void UpdateAddButton() =>
        AddVoiceButton.IsEnabled = _rows.Count < MaxVoices;

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void AddVoice_Click(object sender, RoutedEventArgs e) =>
        AddVoiceRow("", 100f / (_rows.Count + 1));

    private void SpeedSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    { if (SpeedLabel  != null) SpeedLabel.Text  = $"{e.NewValue:F2}"; }

    private void PitchSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    { if (PitchLabel  != null) PitchLabel.Text  = $"{e.NewValue:F1}"; }

    private void VolumeSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    { if (VolumeLabel != null) VolumeLabel.Text = $"{e.NewValue:F2}"; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Speaker name is required.", "GukVoice",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _profile.Name    = NameBox.Text.Trim();
        _profile.Type    = (SpeakerType)(TypeBox.SelectedIndex >= 0 ? TypeBox.SelectedIndex : 0);
        _profile.Enabled = EnabledBox.IsChecked == true;

        var vp   = _profile.VoiceProfile;
        vp.Speed          = (float)SpeedSlider.Value;
        vp.PitchSemitones = (float)PitchSlider.Value;
        vp.Volume         = (float)VolumeSlider.Value;

        var total = _rows.Sum(r => r.WeightSlider.Value);
        vp.Voices.Clear();
        foreach (var row in _rows)
        {
            var name = row.VoiceBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            var weight = total > 0 ? (float)(row.WeightSlider.Value / total) : 1f;
            vp.Voices.Add(new VoiceWeight { Voice = name, Weight = weight });
        }

        DialogResult = true;
    }
}
