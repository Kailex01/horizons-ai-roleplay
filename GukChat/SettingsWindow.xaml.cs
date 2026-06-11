using System.Windows.Forms;
using System.Windows.Input;
using GukChat.Models;
using GukChat.Services;

namespace GukChat;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var s = AppConfig.Current;
        BotAiUrlBox.Text     = s.BotAiBaseUrl;
        SpeakerNameBox.Text  = s.SpeakerName;
        PiperExeBox.Text     = s.PiperExePath;
        PiperModelsBox.Text  = s.PiperModelsPath;
    }

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e) => DragMove();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.Apply(new AppSettings
        {
            BotAiBaseUrl    = BotAiUrlBox.Text.Trim(),
            SpeakerName     = SpeakerNameBox.Text.Trim(),
            PiperExePath    = PiperExeBox.Text.Trim(),
            PiperModelsPath = PiperModelsBox.Text.Trim(),
        });
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BrowsePiperExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select piper.exe",
            Filter = "piper.exe|piper.exe|Executable|*.exe",
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            PiperExeBox.Text = dlg.FileName;
    }

    private void BrowsePiperModels_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FolderBrowserDialog { Description = "Select Piper models folder" };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            PiperModelsBox.Text = dlg.SelectedPath;
    }
}
