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
        ApiKeyBox.Text          = s.OpenRouterApiKey;
        DefaultModelBox.Text    = s.DefaultModel;
        SpeakerNameBox.Text     = s.SpeakerName;
        MaxReplyTokensBox.Text  = s.MaxReplyTokens.ToString();

        foreach (var v in KokoroService.GetInstalledVoiceNames())
            NarratorVoiceBox.Items.Add(v);

        var np = s.NarratorVoiceProfile;
        NarratorVoiceBox.Text         = np.Voices.FirstOrDefault()?.Voice ?? "";
        NarratorSpeedSlider.Value     = Math.Clamp(np.Speed,          0.5, 2.0);
        NarratorPitchSlider.Value     = Math.Clamp(np.PitchSemitones, -12.0, 12.0);
        NarratorVolumeSlider.Value    = Math.Clamp(np.Volume,         0.0, 2.0);
        NarratorSpeedLabel.Text       = np.Speed.ToString("F2");
        NarratorPitchLabel.Text       = np.PitchSemitones.ToString("F1");
        NarratorVolumeLabel.Text      = np.Volume.ToString("F2");

        DefaultCharacterPromptBox.Text = s.DefaultCharacterPrompt;

        NarratorEnabledBox.IsChecked = s.NarratorEnabled;
        NarratorModelBox.Text        = s.NarratorModel;
        NarratorPromptBox.Text       = s.NarratorSystemPrompt;
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e) => DragMove();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var voiceName = NarratorVoiceBox.Text.Trim();
        float speed  = (float)NarratorSpeedSlider.Value;
        float pitch  = (float)NarratorPitchSlider.Value;
        float volume = (float)NarratorVolumeSlider.Value;

        var narratorProfile = new VoiceProfile
        {
            Speed          = speed,
            PitchSemitones = pitch,
            Volume         = volume,
        };
        if (!string.IsNullOrWhiteSpace(voiceName))
            narratorProfile.Voices.Add(new VoiceWeight { Voice = voiceName, Weight = 1f });

        var narratorPrompt = NarratorPromptBox.Text.Trim();
        int.TryParse(MaxReplyTokensBox.Text, out int maxTokens);
        if (maxTokens <= 0) maxTokens = 60;

        AppConfig.Apply(new AppSettings
        {
            OpenRouterApiKey          = ApiKeyBox.Text.Trim(),
            DefaultModel              = DefaultModelBox.Text.Trim(),
            SpeakerName               = SpeakerNameBox.Text.Trim(),
            MaxReplyTokens            = maxTokens,
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

    private void OnNarratorSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender == NarratorSpeedSlider  && NarratorSpeedLabel  != null) NarratorSpeedLabel.Text  = e.NewValue.ToString("F2");
        if (sender == NarratorPitchSlider  && NarratorPitchLabel  != null) NarratorPitchLabel.Text  = e.NewValue.ToString("F1");
        if (sender == NarratorVolumeSlider && NarratorVolumeLabel != null) NarratorVolumeLabel.Text = e.NewValue.ToString("F2");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
