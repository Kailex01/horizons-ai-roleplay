using HorizonsAI.Models;
using HorizonsAI.Services;
using HorizonsAI.ViewModels;

namespace HorizonsAI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.ScrollToBottom += () =>
            Dispatcher.InvokeAsync(
                () => MessagesScroll.ScrollToEnd(),
                System.Windows.Threading.DispatcherPriority.Background);

        _vm.TtsSetupRequested += OnTtsSetupRequested;

        Loaded += (_, _) =>
        {
            _vm.LoadCharacters(); // LoadCharacters calls LoadParties internally
            _vm.LoadScenes();
            if (KokoroService.IsModelReady)
            {
                try { _vm.InitializeTts(); }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Voice model found but failed to load:\n\n{ex.Message}\n\nDelete the data/tts folder and re-download the model.",
                        "Voice Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        };

        Closed += (_, _) => _vm.Dispose();
    }

    // ── TTS setup ─────────────────────────────────────────────────────────────

    private bool _ttsSetupInProgress;
    private void OnTtsSetupRequested()
    {
        if (_ttsSetupInProgress) return;
        _ttsSetupInProgress = true;
        // BeginInvoke defers so we're not running dialog code inside the property setter
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var dlg = new TtsSetupWindow { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    try { _vm.InitializeTts(); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Voice model loaded but failed to initialise:\n\n{ex.Message}\n\nTry restarting the app.",
                            "Voice Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _vm.IsVoiceEnabled = false;
                    }
                }
                else
                    _vm.IsVoiceEnabled = false;
            }
            finally
            {
                _ttsSetupInProgress = false;
            }
        });
    }

    // ── Title bar ──────────────────────────────────────────────────────────────

    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e)   => WindowState = WindowState.Minimized;
    private void MaxRestore_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e)      => Close();
    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ── Character sidebar ──────────────────────────────────────────────────────

    private void CategoryHeader_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is CategoryGroup group)
            group.IsExpanded = !group.IsExpanded;
    }

    private void AddCharacter_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CharacterEditWindow { Owner = this };
        if (dlg.ShowDialog() == true) _vm.LoadCharacters();
    }

    private void EditCharacter_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem<CharacterItem>(sender);
        if (item is null) return;
        var dlg = new CharacterEditWindow(item.Character) { Owner = this };
        if (dlg.ShowDialog() != null) _vm.LoadCharacters();
    }

    private void DeleteCharacter_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem<CharacterItem>(sender);
        if (item != null) _vm.DeleteCharacter(item);
    }

    // ── Party sidebar ──────────────────────────────────────────────────────────

    private void AddParty_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PartyEditWindow { Owner = this };
        if (dlg.ShowDialog() == true) _vm.LoadParties();
    }

    private void EditParty_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem<PartyItem>(sender);
        if (item is null) return;
        var dlg = new PartyEditWindow(item.Party) { Owner = this };
        if (dlg.ShowDialog() != null) _vm.LoadParties();
    }

    private void DeleteParty_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem<PartyItem>(sender);
        if (item != null) _vm.DeleteParty(item);
    }

    // ── Scene sidebar ──────────────────────────────────────────────────────────

    private void AddScene_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SceneEditWindow { Owner = this };
        if (dlg.ShowDialog() == true) _vm.LoadScenes();
    }

    private void EditScene_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem<SceneItem>(sender);
        if (item is null) return;
        var dlg = new SceneEditWindow(item.Scene) { Owner = this };
        if (dlg.ShowDialog() != null) _vm.LoadScenes();
    }

    private void DeleteScene_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextItem<SceneItem>(sender);
        if (item != null) _vm.DeleteScene(item);
    }

    private void PromoteSceneNpc_Click(object sender, RoutedEventArgs e)
    {
        var npc = GetContextItem<SceneNpc>(sender);
        if (npc != null) _vm.PromoteSceneNpc(npc);
    }

    // ── Play-as picker ────────────────────────────────────────────────────────

    private void PlayAsBtn_Click(object sender, RoutedEventArgs e)
        => PlayAsPopup.IsOpen = !PlayAsPopup.IsOpen;

    private void PlayAsPlayer_Click(object sender, RoutedEventArgs e)
    {
        _vm.SetPlayAs(null);
        PlayAsPopup.IsOpen = false;
    }

    private void PlayAsCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is CharacterItem item)
            _vm.SetPlayAs(item);
        PlayAsPopup.IsOpen = false;
    }

    // ── Dialogs ────────────────────────────────────────────────────────────────

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow { Owner = this };
        if (dlg.ShowDialog() == true) _vm.OnSettingsChanged();
    }

    private void Lorebook_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new LorebookWindow { Owner = this };
        dlg.ShowDialog();
        _vm.LoadLorebook();
    }

    // ── Message edit ──────────────────────────────────────────────────────────

    private void EditMessage_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetContextItem<ChatMessageVm>(sender);
        vm?.BeginEdit();
    }

    // ── Input ──────────────────────────────────────────────────────────────────

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.SendCommand.CanExecute(null))
        {
            _vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static T? GetContextItem<T>(object sender) where T : class
    {
        if (sender is MenuItem mi && mi.Parent is ContextMenu cm)
            return cm.DataContext as T;
        return null;
    }
}
