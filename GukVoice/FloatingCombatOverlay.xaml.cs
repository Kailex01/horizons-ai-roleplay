using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
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

    private const double TravelDistance = 140.0;

    private readonly Random _rng = new();


    // ── Debug origin crosshair ─────────────────────────────────────────────────
    private readonly Line      _debugH     = new() { Stroke = Brushes.Red, StrokeThickness = 1.5 };
    private readonly Line      _debugV     = new() { Stroke = Brushes.Red, StrokeThickness = 1.5 };
    private readonly Ellipse   _debugDot   = new() { Width = 8, Height = 8, Fill = Brushes.Red };
    private readonly TextBlock _debugLabel = new()
    {
        Text       = "FCT origin",
        FontSize   = 10,
        Foreground = Brushes.Red,
        Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
    };

    public FloatingCombatOverlay()
    {
        InitializeComponent();
        // Add debug elements first so they render behind floating text
        OverlayCanvas.Children.Add(_debugH);
        OverlayCanvas.Children.Add(_debugV);
        OverlayCanvas.Children.Add(_debugDot);
        OverlayCanvas.Children.Add(_debugLabel);
        RefreshDebugMarker();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd  = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    // ── Called by MainWindow when EQ window moves / resizes ───────────────────
    // GetClientRect/ClientToScreen return physical pixels (our process is DPI-aware).
    // WPF Window properties expect logical units. Read the scale factor each call
    // so it stays correct if the window moves to a different monitor.
    public void UpdatePosition(Rect eqRect)
    {
        var src = PresentationSource.FromVisual(this);
        double sx = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double sy = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;
        Left   = eqRect.Left   * sx;
        Top    = eqRect.Top    * sy;
        Width  = eqRect.Width  * sx;
        Height = eqRect.Height * sy;
        RefreshDebugMarker();
    }

    // ── Reposition / show-hide the debug crosshair ────────────────────────────
    public void RefreshDebugMarker()
    {
        var s   = AppConfig.Current.Fct;
        bool on = s.ShowDebugOrigin;

        var vis = on ? Visibility.Visible : Visibility.Collapsed;
        _debugH.Visibility     = vis;
        _debugV.Visibility     = vis;
        _debugDot.Visibility   = vis;
        _debugLabel.Visibility = vis;

        if (!on) return;

        double cx = OverlayCanvas.ActualWidth  / 2.0 + s.OriginOffsetX;
        double cy = OverlayCanvas.ActualHeight / 2.0 + s.OriginOffsetY;

        _debugH.X1 = cx - 18; _debugH.X2 = cx + 18;
        _debugH.Y1 = cy;      _debugH.Y2 = cy;

        _debugV.X1 = cx; _debugV.X2 = cx;
        _debugV.Y1 = cy - 18; _debugV.Y2 = cy + 18;

        Canvas.SetLeft(_debugDot,   cx - 4);
        Canvas.SetTop (_debugDot,   cy - 4);
        _debugLabel.Text = $"FCT origin  canvas:{OverlayCanvas.ActualWidth:0}×{OverlayCanvas.ActualHeight:0}";
        Canvas.SetLeft(_debugLabel, cx + 6);
        Canvas.SetTop (_debugLabel, cy - 14);
    }

    // ── Called by FctViewModel via SpawnRequested event ───────────────────────
    public void Spawn(FctSpawnArgs args)
    {
        Dispatcher.Invoke(() =>
        {
            var style = GetStyle(args.Category);

            var tb = new TextBlock
            {
                Text       = args.Text,
                FontSize   = style.StartSize,
                FontWeight = style.Weight,
                Foreground = new SolidColorBrush(style.Color),
                FontFamily = new FontFamily("Segoe UI"),
                Effect     = new DropShadowEffect
                {
                    Color       = Colors.Black,
                    BlurRadius  = 4,
                    ShadowDepth = 1,
                    Opacity     = 0.9,
                },
            };

            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textW = tb.DesiredSize.Width;
            double textH = tb.DesiredSize.Height;

            var s = AppConfig.Current.Fct;
            double cx = OverlayCanvas.ActualWidth  / 2.0 + s.OriginOffsetX;
            double cy = OverlayCanvas.ActualHeight / 2.0 + s.OriginOffsetY;

            // Small scatter perpendicular to travel direction + ±5° angle variance
            double scatter   = _rng.Next(-20, 20);
            double angleVar  = _rng.Next(-5, 6);
            double rad       = (style.AngleDeg + angleVar) * Math.PI / 180.0;
            double dx      = Math.Sin(rad);
            double dy      = -Math.Cos(rad);
            double perpDx  = -dy;
            double perpDy  = dx;

            double startX = cx + scatter * perpDx - textW / 2.0;
            double startY = cy + scatter * perpDy - textH / 2.0;

            Canvas.SetLeft(tb, startX);
            Canvas.SetTop (tb, startY);
            OverlayCanvas.Children.Add(tb);

            var dur     = TimeSpan.FromSeconds(style.Duration);
            var halfDur = TimeSpan.FromSeconds(style.Duration * 0.5);
            var sb      = new Storyboard();

            if (style.Parabolic)
            {
                // Arc up then fall hard — larger drop for more dramatic gravity
                var yAnim = new DoubleAnimationUsingKeyFrames();
                yAnim.KeyFrames.Add(new LinearDoubleKeyFrame(startY,       KeyTime.FromPercent(0.0)));
                yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(startY - 90,  KeyTime.FromPercent(0.35))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(startY + 180, KeyTime.FromPercent(1.0))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                Storyboard.SetTarget(yAnim, tb);
                Storyboard.SetTargetProperty(yAnim, new PropertyPath(Canvas.TopProperty));
                sb.Children.Add(yAnim);
            }
            else
            {
                // X: constant travel in the direction angle
                var xAnim = new DoubleAnimation(startX, startX + dx * TravelDistance, dur)
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(xAnim, tb);
                Storyboard.SetTargetProperty(xAnim, new PropertyPath(Canvas.LeftProperty));
                sb.Children.Add(xAnim);

                // Y: reach travel peak then fall an extra 110px due to gravity
                double yPeak = startY + dy * TravelDistance;
                var yAnim = new DoubleAnimationUsingKeyFrames();
                yAnim.KeyFrames.Add(new LinearDoubleKeyFrame(startY, KeyTime.FromPercent(0.0)));
                yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(yPeak,  KeyTime.FromPercent(0.45))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(yPeak + 110, KeyTime.FromPercent(1.0))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                Storyboard.SetTarget(yAnim, tb);
                Storyboard.SetTargetProperty(yAnim, new PropertyPath(Canvas.TopProperty));
                sb.Children.Add(yAnim);
            }

            // Font size grows during flight
            var sizeAnim = new DoubleAnimation(style.StartSize, style.EndSize, dur)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sizeAnim, tb);
            Storyboard.SetTargetProperty(sizeAnim, new PropertyPath(TextBlock.FontSizeProperty));
            sb.Children.Add(sizeAnim);

            // Fade out during the second half
            var fadeAnim = new DoubleAnimation(1.0, 0.0, new Duration(halfDur))
                { BeginTime = halfDur };
            Storyboard.SetTarget(fadeAnim, tb);
            Storyboard.SetTargetProperty(fadeAnim, new PropertyPath(OpacityProperty));
            sb.Children.Add(fadeAnim);

            sb.Completed += (_, _) => OverlayCanvas.Children.Remove(tb);
            sb.Begin();
        });
    }

    // ── Style table ───────────────────────────────────────────────────────────
    // StartSize is read from settings (user-editable, defaults in FctSettings).
    // EndSize is a fixed ratio of StartSize so changing start scales the animation.
    // AngleDeg: 0=up, 90=right, 180=down, 270=left — clockwise from top.

    private record StyleDef(
        Color Color, double StartSize, double EndSize,
        FontWeight Weight, double Duration, double AngleDeg, bool Parabolic = false);

    private static StyleDef GetStyle(FctCategory cat)
    {
        var fs = AppConfig.Current.Fct;
        return cat switch
        {
            FctCategory.DamageOut    => new(ParseColor(fs.ColorDamageOut),    fs.FontSizeDamageOut,    fs.FontSizeDamageOut    * 1.28, FontWeights.Normal, 2.2,  65),
            FctCategory.DamageIn     => new(ParseColor(fs.ColorDamageIn),     fs.FontSizeDamageIn,     fs.FontSizeDamageIn     * 1.28, FontWeights.Normal, 2.2, 300),
            FctCategory.CritOut      => new(ParseColor(fs.ColorCritOut),      fs.FontSizeCritOut,      fs.FontSizeCritOut      * 2.0,  FontWeights.Bold,   3.0,  45),
            FctCategory.CritIn       => new(ParseColor(fs.ColorCritIn),       fs.FontSizeCritIn,       fs.FontSizeCritIn       * 2.0,  FontWeights.Bold,   3.0, 315),
            FctCategory.SpellOut     => new(ParseColor(fs.ColorSpellOut),     fs.FontSizeSpellOut,     fs.FontSizeSpellOut     * 1.28, FontWeights.Normal, 2.2,  65),
            FctCategory.SpellIn      => new(ParseColor(fs.ColorSpellIn),      fs.FontSizeSpellIn,      fs.FontSizeSpellIn      * 1.28, FontWeights.Normal, 2.2, 300),
            FctCategory.HealFriendly => new(ParseColor(fs.ColorHealFriendly), fs.FontSizeHealFriendly, fs.FontSizeHealFriendly * 1.28, FontWeights.Normal, 2.2,  15),
            FctCategory.HealEnemy    => new(ParseColor(fs.ColorHealEnemy),    fs.FontSizeHealEnemy,    fs.FontSizeHealEnemy    * 1.25, FontWeights.Normal, 2.2, 345),
            FctCategory.LevelUp      => new(ParseColor(fs.ColorLevelUp),      fs.FontSizeLevelUp,      fs.FontSizeLevelUp      * 1.47, FontWeights.Bold,   4.5,   0, Parabolic: true),
            FctCategory.ExpGain      => new(ParseColor(fs.ColorExpGain),      fs.FontSizeExpGain,      fs.FontSizeExpGain      * 1.23, FontWeights.Normal, 2.0,   0, Parabolic: true),
            _                        => new(Colors.White,                      16,                      20,                             FontWeights.Normal, 2.2,   0),
        };
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.White; }
    }
}
