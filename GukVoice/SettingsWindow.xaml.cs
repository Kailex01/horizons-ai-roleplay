using GukVoice.Models;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Media;

namespace GukVoice;

public partial class SettingsWindow : Window
{
    private bool            _adjustingWeights;
    private const int       MaxVoices = 5;
    private readonly string[] _voiceNames;

    private class VoiceRow
    {
        public required Grid      Container;
        public required ComboBox  VoiceBox;
        public required Slider    WeightSlider;
        public required TextBlock WeightLabel;
    }

    private readonly List<VoiceRow> _rows = new();

    public SettingsWindow()
    {
        _voiceNames = KokoroService.GetInstalledVoiceNames(AppConfig.TtsFolder);
        InitializeComponent();
        Load();
    }

    // ── Load ───────────────────────────────────────────────────────────────────

    private void Load()
    {
        var s = AppConfig.Current;
        LogPathBox.Text       = s.EqLogPath;
        PlayerNameBox.Text    = s.PlayerName;
        ArchiveFolderBox.Text = s.ArchiveFolder;
        ArchiveOnExitBox.IsChecked = s.ArchiveOnEqExit;

        var vp = s.NarratorVoice;
        if (vp != null)
        {
            SpeedSlider.Value  = Math.Clamp(vp.Speed,          0.5, 2.0);
            PitchSlider.Value  = Math.Clamp(vp.PitchSemitones, -12, 12);
            VolumeSlider.Value = Math.Clamp(vp.Volume,         0.1, 2.0);
            foreach (var vw in vp.Voices)
                AddVoiceRow(vw.Voice, vw.Weight * 100f);
        }

        if (_rows.Count == 0) AddVoiceRow("", 100f);
        UpdateAddButton();
    }

    // ── Voice rows (same pattern as SpeakerEditWindow) ─────────────────────────

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

        var voiceBox = new ComboBox
        {
            Background      = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x42)),
            Foreground      = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xF0)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x58)),
            BorderThickness = new Thickness(1),
        };
        foreach (var v in _voiceNames) voiceBox.Items.Add(v);
        voiceBox.Text = voice;
        Grid.SetColumn(voiceBox, 0);

        var weightSlider = new Slider
        {
            Minimum = 1, Maximum = 100, Value = Math.Clamp(weightPct, 1, 100),
            TickFrequency = 1, IsSnapToTickEnabled = true,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xBF)),
        };
        Grid.SetColumn(weightSlider, 2);

        var weightLabel = new TextBlock
        {
            Text                = $"{(int)weightPct}%",
            Foreground          = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xF0)),
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(weightLabel, 4);

        var removeBtn = new Button
        {
            Content         = "✕",
            Background      = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
            Foreground      = new SolidColorBrush(Color.FromRgb(0xDD, 0x44, 0x44)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x8A)),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(0),
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

    // ── Browse handlers ────────────────────────────────────────────────────────

    private void BrowseLog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select EverQuest Log File",
            Filter = "Log files (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true) LogPathBox.Text = dlg.FileName;
    }

    private void BrowseArchive_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title           = "Select Archive Folder (pick any file inside it)",
            Filter          = "All files (*.*)|*.*",
            CheckFileExists = false,
            FileName        = "Select Folder",
        };
        if (dlg.ShowDialog() == true)
            ArchiveFolderBox.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
    }

    // ── Slider handlers (null-guarded — fire during InitializeComponent) ───────

    private void SpeedSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    { if (SpeedLabel  != null) SpeedLabel.Text  = $"{e.NewValue:F2}"; }

    private void PitchSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    { if (PitchLabel  != null) PitchLabel.Text  = $"{e.NewValue:F1}"; }

    private void VolumeSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    { if (VolumeLabel != null) VolumeLabel.Text = $"{e.NewValue:F2}"; }

    private void AddVoice_Click(object sender, RoutedEventArgs e) =>
        AddVoiceRow("", 100f / (_rows.Count + 1));

    // ── Save ───────────────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LogPathBox.Text))
        {
            MessageBox.Show("Please select the EQ log file.", "GukVoice",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Build narrator voice profile — null if no voices configured
        VoiceProfile? narratorVoice = null;
        var total = _rows.Sum(r => r.WeightSlider.Value);
        var voiceList = _rows
            .Select(r => r.VoiceBox.Text.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        if (voiceList.Count > 0)
        {
            var vp = new VoiceProfile
            {
                Speed          = (float)SpeedSlider.Value,
                PitchSemitones = (float)PitchSlider.Value,
                Volume         = (float)VolumeSlider.Value,
            };
            foreach (var row in _rows)
            {
                var name = row.VoiceBox.Text.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                vp.Voices.Add(new VoiceWeight
                {
                    Voice  = name,
                    Weight = total > 0 ? (float)(row.WeightSlider.Value / total) : 1f,
                });
            }
            narratorVoice = vp;
        }

        AppConfig.Apply(new GukVoiceSettings
        {
            EqLogPath       = LogPathBox.Text.Trim(),
            PlayerName      = PlayerNameBox.Text.Trim(),
            ArchiveFolder   = ArchiveFolderBox.Text.Trim(),
            ArchiveOnEqExit = ArchiveOnExitBox.IsChecked == true,
            Speakers        = AppConfig.Current.Speakers,
            ZoneVoice       = AppConfig.Current.ZoneVoice,
            ExpVoice        = AppConfig.Current.ExpVoice,
            LootVoice       = AppConfig.Current.LootVoice,
            NarratorVoice   = narratorVoice,
        });

        DialogResult = true;
    }
}
