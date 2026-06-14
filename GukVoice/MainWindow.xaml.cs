using GukVoice.ViewModels;
using System.ComponentModel;
using System.Windows.Forms;

namespace GukVoice;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly NotifyIcon    _tray;
    private bool                   _isExiting;

    // Tray menu items we need to update dynamically
    private readonly ToolStripMenuItem _monitorMenuItem;
    private readonly ToolStripMenuItem _showHideMenuItem;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _tray = BuildTrayIcon(out _monitorMenuItem, out _showHideMenuItem);

        StateChanged += OnStateChanged;
        Closed       += OnClosed;
    }

    // ── Tray icon setup ────────────────────────────────────────────────────────

    private NotifyIcon BuildTrayIcon(out ToolStripMenuItem monitorItem,
                                      out ToolStripMenuItem showHideItem)
    {
        var menu = new ContextMenuStrip();

        // Title row (disabled — acts as a label)
        menu.Items.Add(new ToolStripMenuItem("GukVoice") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        // Start / Pause monitoring
        monitorItem       = new ToolStripMenuItem();
        monitorItem.Click += (_, _) => _vm.ToggleMonitoringCommand.Execute(null);
        menu.Items.Add(monitorItem);

        menu.Items.Add(new ToolStripSeparator());

        // Archive log
        var archiveItem   = new ToolStripMenuItem("🗄  Archive & Clear Log");
        archiveItem.Click += (_, _) => _vm.ArchiveLogCommand.Execute(null);
        menu.Items.Add(archiveItem);

        menu.Items.Add(new ToolStripSeparator());

        // Settings
        var settingsItem   = new ToolStripMenuItem("⚙  Settings…");
        settingsItem.Click += (_, _) => _vm.OpenSettingsCommand.Execute(null);
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        // Show / Hide window
        showHideItem       = new ToolStripMenuItem();
        showHideItem.Click += (_, _) => ToggleVisibility();
        menu.Items.Add(showHideItem);

        menu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem   = new ToolStripMenuItem("Exit GukVoice");
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        // Refresh text each time the menu opens
        menu.Opening += (_, _) => RefreshMenuText();

        var icon = new NotifyIcon
        {
            Icon             = System.Drawing.SystemIcons.Application,
            Text             = "GukVoice",
            Visible          = true,
            ContextMenuStrip = menu,
        };

        icon.DoubleClick += (_, _) => ToggleVisibility();
        return icon;
    }

    private void RefreshMenuText()
    {
        _monitorMenuItem.Text  = _vm.IsWatching ? "⏸  Pause Monitoring" : "▶  Start Monitoring";
        _showHideMenuItem.Text = IsVisible        ? "Hide Window"          : "Show Window";
    }

    // ── Visibility / minimize-to-tray ──────────────────────────────────────────

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Minimize to tray instead of taskbar
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    // ── Closing behavior ───────────────────────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting)
        {
            // X button hides to tray rather than quitting
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _tray.Visible = false;
        _tray.Dispose();
        _vm.Dispose();
    }

    private void ExitApp()
    {
        _isExiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }
}
