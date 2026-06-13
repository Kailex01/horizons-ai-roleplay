using System.Windows.Input;
using HorizonsAI.Models;
using HorizonsAI.Services;

namespace HorizonsAI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var s = AppConfig.Current;
        ApiKeyBox.Text       = s.OpenRouterApiKey;
        DefaultModelBox.Text = s.DefaultModel;
        SpeakerNameBox.Text  = s.SpeakerName;

        foreach (var v in KokoroService.GetInstalledVoiceNames())
            NarratorVoiceBox.Items.Add(v);

        var np = s.NarratorVoiceProfile;
        NarratorVoiceBox.Text  = np.Voices.FirstOrDefault()?.Voice ?? "";
        NarratorSpeedBox.Text  = np.Speed.ToString("F1");
        NarratorPitchBox.Text  = np.PitchSemitones.ToString("F1");
        NarratorVolumeBox.Text = np.Volume.ToString("F1");

        DefaultCharacterPromptBox.Text = s.DefaultCharacterPrompt;

        NarratorEnabledBox.IsChecked = s.NarratorEnabled;
        NarratorModelBox.Text        = s.NarratorModel;
        NarratorPromptBox.Text       = s.NarratorSystemPrompt;
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e) => DragMove();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var voiceName = NarratorVoiceBox.Text.Trim();
        float.TryParse(NarratorSpeedBox.Text,  out float speed);  if (speed  <= 0) speed  = 1f;
        float.TryParse(NarratorPitchBox.Text,  out float pitch);
        float.TryParse(NarratorVolumeBox.Text, out float volume); if (volume <= 0) volume = 1f;

        var narratorProfile = new VoiceProfile
        {
            Speed          = speed,
            PitchSemitones = pitch,
            Volume         = volume,
        };
        if (!string.IsNullOrWhiteSpace(voiceName))
            narratorProfile.Voices.Add(new VoiceWeight { Voice = voiceName, Weight = 1f });

        var narratorPrompt = NarratorPromptBox.Text.Trim();
        AppConfig.Apply(new AppSettings
        {
            OpenRouterApiKey          = ApiKeyBox.Text.Trim(),
            DefaultModel              = DefaultModelBox.Text.Trim(),
            SpeakerName               = SpeakerNameBox.Text.Trim(),
            NarratorVoiceProfile      = narratorProfile,
            DefaultCharacterPrompt    = DefaultCharacterPromptBox.Text.Trim(),
            NarratorEnabled           = NarratorEnabledBox.IsChecked == true,
            NarratorModel             = NarratorModelBox.Text.Trim(),
            NarratorSystemPrompt      = string.IsNullOrWhiteSpace(narratorPrompt)
                                        ? AppSettings.DefaultNarratorPrompt
                                        : narratorPrompt,
        });
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
