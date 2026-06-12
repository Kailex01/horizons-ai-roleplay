using System.IO;
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

    // English v0.19 voices
    private static readonly string[] VoicesEn =
    [
        "af", "af_bella", "af_nicole", "af_sarah", "af_sky",
        "am_adam", "am_michael",
        "bf_emma", "bf_isabella",
        "bm_george", "bm_lewis",
    ];

    // Multilingual v1.0 voices (53 total)
    private static readonly string[] VoicesV10 =
    [
        // American Female
        "af_alloy", "af_aoede", "af_bella", "af_heart", "af_jessica",
        "af_kore",  "af_nicole", "af_nova", "af_river", "af_sarah", "af_sky",
        // American Male
        "am_adam", "am_echo", "am_eric", "am_fenrir", "am_liam",
        "am_michael", "am_onyx", "am_puck", "am_santa",
        // British Female
        "bf_alice", "bf_emma", "bf_isabella", "bf_lily",
        // British Male
        "bm_daniel", "bm_fable", "bm_george", "bm_lewis",
        // Spanish
        "ef_dora", "em_alex",
        // French
        "ff_siwis",
        // Hindi
        "hf_alpha", "hf_beta", "hm_omega", "hm_psi",
        // Italian
        "if_sara", "im_nicola",
        // Japanese Female
        "jf_alpha", "jf_gongitsune", "jf_nezumi", "jf_tebukuro",
        // Japanese Male
        "jm_kumo",
        // Portuguese
        "pf_dora", "pm_alex", "pm_santa",
        // Chinese Female
        "zf_xiaobei", "zf_xiaoni", "zf_xiaoxiao", "zf_xiaoyi",
        // Chinese Male
        "zm_yunjian", "zm_yunxi", "zm_yunxia", "zm_yunyang",
    ];

    // Multilingual v1.1 voices (103 total: 3 EN + 55 ZH-F + 45 ZH-M)
    private static readonly string[] VoicesV11 = BuildVoicesV11();
    private static string[] BuildVoicesV11()
    {
        var list = new System.Collections.Generic.List<string> { "af_maple", "af_sol", "bf_vale" };
        for (int i = 1;  i <= 55; i++) list.Add($"zf_{i:000}");
        for (int i = 9;  i <= 53; i++) list.Add($"zm_{i:000}");
        return list.ToArray();
    }

    private static string[] GetKokoroVoices()
    {
        var markerFile = Path.Combine(AppConfig.TtsFolder, "model_type.txt");
        var modelType  = File.Exists(markerFile) ? File.ReadAllText(markerFile).Trim() : "";
        return modelType switch
        {
            "multi-v1_1" => VoicesV11,
            "multi-v1_0" => VoicesV10,
            "en-v0_19"   => VoicesEn,
            _            => VoicesV10, // default to v1.0 list if no model installed
        };
    }

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
        foreach (var v in GetKokoroVoices()) voiceBox.Items.Add(v);

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
