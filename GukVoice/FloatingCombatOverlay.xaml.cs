using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using GukVoice.ViewModels;

namespace GukVoice;

public partial class FloatingCombatOverlay : Window
{
    // ── Win32: make the window click-through ───────────────────────────────────
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED     = 0x00080000;

    private readonly Random _rng = new();

    public FloatingCombatOverlay()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd  = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    // ── Called by MainWindow when EQ window moves / resizes ───────────────────
    public void UpdatePosition(Rect eqRect)
    {
        Left   = eqRect.Left;
        Top    = eqRect.Top;
        Width  = eqRect.Width;
        Height = eqRect.Height;
    }

    // ── Called by FctViewModel via SpawnRequested event ───────────────────────
    public void Spawn(FctSpawnArgs args)
    {
        Dispatcher.Invoke(() =>
        {
            var (color, fontSize, fontWeight, duration) = GetStyle(args.Category);

            var tb = new System.Windows.Controls.TextBlock
            {
                Text       = args.Text,
                FontSize   = fontSize,
                FontWeight = fontWeight,
                Foreground = new SolidColorBrush(color),
                FontFamily = new FontFamily("Segoe UI"),
                Effect     = new DropShadowEffect
                {
                    Color       = Colors.Black,
                    BlurRadius  = 4,
                    ShadowDepth = 1,
                    Opacity     = 0.9,
                },
            };

            // Measure so we can centre the text on the spawn point
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textW = tb.DesiredSize.Width;

            double cx     = OverlayCanvas.ActualWidth  / 2.0;
            double cy     = OverlayCanvas.ActualHeight / 2.0;
            double startX = cx + _rng.Next(-90, 90) - textW / 2.0;
            double startY = cy - 40;

            Canvas.SetLeft(tb, startX);
            Canvas.SetTop(tb, startY);
            OverlayCanvas.Children.Add(tb);

            var sb = new Storyboard();

            // Float upward
            var moveAnim = new DoubleAnimation(startY, startY - 130,
                new Duration(TimeSpan.FromSeconds(duration)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(moveAnim, tb);
            Storyboard.SetTargetProperty(moveAnim, new PropertyPath(Canvas.TopProperty));

            // Fade out during the second half
            var fadeAnim = new DoubleAnimation(1.0, 0.0,
                new Duration(TimeSpan.FromSeconds(duration * 0.5)))
            {
                BeginTime = TimeSpan.FromSeconds(duration * 0.5),
            };
            Storyboard.SetTarget(fadeAnim, tb);
            Storyboard.SetTargetProperty(fadeAnim, new PropertyPath(OpacityProperty));

            sb.Children.Add(moveAnim);
            sb.Children.Add(fadeAnim);
            sb.Completed += (_, _) => OverlayCanvas.Children.Remove(tb);
            sb.Begin();
        });
    }

    // ── Style table ───────────────────────────────────────────────────────────

    private static (Color color, double fontSize, FontWeight fontWeight, double duration)
        GetStyle(FctCategory cat) => cat switch
    {
        FctCategory.DamageOut    => (Color.FromRgb(0xFF, 0xFF, 0xFF), 18, FontWeights.Normal, 1.4),
        FctCategory.DamageIn     => (Color.FromRgb(0xFF, 0x70, 0x43), 18, FontWeights.Normal, 1.4),
        FctCategory.CritOut      => (Color.FromRgb(0xFF, 0xD7, 0x00), 26, FontWeights.Bold,   2.0),
        FctCategory.CritIn       => (Color.FromRgb(0xFF, 0x30, 0x30), 26, FontWeights.Bold,   2.0),
        FctCategory.SpellOut     => (Color.FromRgb(0x64, 0xB5, 0xF6), 18, FontWeights.Normal, 1.4),
        FctCategory.SpellIn      => (Color.FromRgb(0xCE, 0x93, 0xD8), 18, FontWeights.Normal, 1.4),
        FctCategory.HealFriendly => (Color.FromRgb(0x81, 0xC7, 0x84), 18, FontWeights.Normal, 1.4),
        FctCategory.HealEnemy    => (Color.FromRgb(0xCD, 0xDC, 0x39), 16, FontWeights.Normal, 1.4),
        FctCategory.LevelUp      => (Color.FromRgb(0xFF, 0xD7, 0x00), 30, FontWeights.Bold,   3.0),
        FctCategory.ExpGain      => (Color.FromRgb(0xFF, 0xF5, 0x9D), 13, FontWeights.Normal, 1.2),
        _                        => (Colors.White,                     16, FontWeights.Normal, 1.4),
    };
}
