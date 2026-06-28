using GukVoice.ViewModels;
using System.ComponentModel;
using System.Windows.Forms;

namespace GukVoice;

public partial class MainWindow : Window
{
    private readonly MainViewModel      _vm = new();
    private readonly NotifyIcon         _tray;
    private FloatingCombatOverlay?      _overlay;
    private bool                        _isExiting;

    // Tray menu items we need to update dynamically
    private readonly ToolStripMenuItem _monitorMenuItem;
    private readonly ToolStripMenuItem _showHideMenuItem;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _tray = BuildTrayIcon(out _monitorMenuItem, out _showHideMenuItem);
        InitOverlay();

        StateChanged += OnStateChanged;
        Closed       += OnClosed;
    }

    // ── FCT overlay ────────────────────────────────────────────────────────────

    private void InitOverlay()
    {
        _overlay = new FloatingCombatOverlay();

        // Subscribe before the initial Show so UpdatePosition is wired first
        _vm.EqWindowMoved      += rect => Dispatcher.Invoke(() => _overlay.UpdatePosition(rect));
        _vm.Fct.SpawnRequested += args => _overlay.Spawn(args);

        // EQ closed — always hide
        _vm.EqClosed += () => Dispatcher.Invoke(() => _overlay.Hide());

        // EQ started — show if enabled, then sync position
        _vm.EqStarted += () => Dispatcher.Invoke(() =>
        {
            if (!_vm.Fct.Enabled) return;
            ShowAndSync();
        });

        // User toggled the Enable checkbox
        _vm.Fct.EnabledChanged += enabled => Dispatcher.Invoke(() =>
        {
            if (enabled && _vm.IsEqRunning) ShowAndSync();
            else                             _overlay.Hide();
        });

        // Origin offset or debug toggle changed — reposition crosshair
        _vm.Fct.OriginChanged += () => Dispatcher.Invoke(() => _overlay.RefreshDebugMarker());

        // Show immediately if FCT is enabled and EQ is already running
        if (_vm.Fct.Enabled && _vm.IsEqRunning)
            ShowAndSync();
    }

    // Show the overlay then immediately apply the current EQ window rect.
    // RectChanged fires during startup before InitOverlay subscribes, so the
    // first event is lost — this call catches up using the tracker's saved rect.
    private void ShowAndSync()
    {
        if (_overlay is null) return;
        _overlay.Show();
        var r = _vm.CurrentEqRect;
        if (!r.IsEmpty) _overlay.UpdatePosition(r);
    }

    // ── Color picker ──────────────────────────────────────────────────────────

    private void OnColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        var cat = Enum.Parse<FctCategory>((string)btn.Tag);
        var hex = _vm.Fct.GetColorHex(cat);

        // Parse current color to a System.Drawing.Color for the dialog
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);

        using var dlg = new System.Windows.Forms.ColorDialog
        {
            Color    = System.Drawing.Color.FromArgb(r, g, b),
            FullOpen = true,
            AnyColor = true,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        _vm.Fct.SetColor(cat, $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
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
        _overlay?.Close();
        _vm.Dispose();
    }

    private void ExitApp()
    {
        _isExiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }
}
