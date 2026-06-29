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

    private const double TravelDistance = 210.0; // 140 × 1.5 — push speed increased 50%

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

            var s   = AppConfig.Current.Fct;
            // If global Bold is on, bump Normal → Bold but leave heavier weights (Bold, ExtraBold) alone
            var weight = (s.GlobalBold && style.Weight == FontWeights.Normal) ? FontWeights.Bold : style.Weight;

            var ot = new OutlinedText
            {
                Text            = args.Text,
                FontSize        = style.StartSize,
                FontWeight      = weight,
                FontStyle       = s.GlobalItalic ? FontStyles.Italic : FontStyles.Normal,
                FontFamilyName  = s.FontFamily,
                Foreground      = new SolidColorBrush(style.Color),
                StrokeBrush     = new SolidColorBrush(style.StrokeColor),
                StrokeThickness = 1.5,
            };

            ot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textW = ot.DesiredSize.Width;
            double textH = ot.DesiredSize.Height;

            double cx = OverlayCanvas.ActualWidth  / 2.0 + s.OriginOffsetX;
            double cy = OverlayCanvas.ActualHeight / 2.0 + s.OriginOffsetY;

            // Small scatter perpendicular to travel direction + configurable angle variance
            int    spread   = s.AngleSpread;
            double scatter  = _rng.Next(-20, 20);
            double angleVar = spread > 0 ? _rng.Next(-spread, spread + 1) : 0;
            double rad      = (style.AngleDeg + angleVar) * Math.PI / 180.0;
            double dx       = Math.Sin(rad);
            double dy       = -Math.Cos(rad);
            double perpDx   = -dy;
            double perpDy   = dx;

            double startX = cx + scatter * perpDx - textW / 2.0;
            double startY = cy + scatter * perpDy - textH / 2.0;

            Canvas.SetLeft(ot, startX);
            Canvas.SetTop (ot, startY);
            OverlayCanvas.Children.Add(ot);

            var dur     = TimeSpan.FromSeconds(style.Duration);
            var halfDur = TimeSpan.FromSeconds(style.Duration * 0.5);
            var sb      = new Storyboard();

            // Fall target: halfway between the peak and the canvas bottom (half fall speed)
            double yCanvasBottom = OverlayCanvas.ActualHeight + textH;

            if (style.Parabolic)
            {
                double yParabolicPeak = startY - 90;
                double yParabolicFall = yParabolicPeak + (yCanvasBottom - yParabolicPeak) / 2.0;
                var yAnim = new DoubleAnimationUsingKeyFrames();
                yAnim.KeyFrames.Add(new LinearDoubleKeyFrame(startY,          KeyTime.FromPercent(0.0)));
                yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(yParabolicPeak,  KeyTime.FromPercent(0.35))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(yParabolicFall,  KeyTime.FromPercent(1.0))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                Storyboard.SetTarget(yAnim, ot);
                Storyboard.SetTargetProperty(yAnim, new PropertyPath(Canvas.TopProperty));
                sb.Children.Add(yAnim);
            }
            else
            {
                // X: drift along the angle direction and decelerate
                var xAnim = new DoubleAnimation(startX, startX + dx * TravelDistance, dur)
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(xAnim, ot);
                Storyboard.SetTargetProperty(xAnim, new PropertyPath(Canvas.LeftProperty));
                sb.Children.Add(xAnim);

                // Y: rise to peak (EaseOut) then fall halfway to canvas bottom (EaseIn)
                double yPeak = startY + dy * TravelDistance;
                double yFall = yPeak + (yCanvasBottom - yPeak) / 2.0;
                var yAnim = new DoubleAnimationUsingKeyFrames();
                yAnim.KeyFrames.Add(new LinearDoubleKeyFrame(startY, KeyTime.FromPercent(0.0)));
                yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(yPeak,  KeyTime.FromPercent(0.40))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(yFall,  KeyTime.FromPercent(1.0))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                Storyboard.SetTarget(yAnim, ot);
                Storyboard.SetTargetProperty(yAnim, new PropertyPath(Canvas.TopProperty));
                sb.Children.Add(yAnim);
            }

            // Font size grows during flight
            var sizeAnim = new DoubleAnimation(style.StartSize, style.EndSize, dur)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(sizeAnim, ot);
            Storyboard.SetTargetProperty(sizeAnim, new PropertyPath(OutlinedText.FontSizeProperty));
            sb.Children.Add(sizeAnim);

            // Fade out during the second half
            var fadeAnim = new DoubleAnimation(1.0, 0.0, new Duration(halfDur))
                { BeginTime = halfDur };
            Storyboard.SetTarget(fadeAnim, ot);
            Storyboard.SetTargetProperty(fadeAnim, new PropertyPath(OpacityProperty));
            sb.Children.Add(fadeAnim);

            sb.Completed += (_, _) => OverlayCanvas.Children.Remove(ot);
            sb.Begin();
        });
    }

    // ── Style table ───────────────────────────────────────────────────────────
    // StartSize is read from settings (user-editable, defaults in FctSettings).
    // EndSize is a fixed ratio of StartSize so changing start scales the animation.
    // AngleDeg: 0=up, 90=right, 180=down, 270=left — clockwise from top.

    private record StyleDef(
        Color Color, Color StrokeColor, double StartSize, double EndSize,
        FontWeight Weight, double Duration, double AngleDeg, bool Parabolic = false);

    private static StyleDef GetStyle(FctCategory cat)
    {
        var fs = AppConfig.Current.Fct;
        return cat switch
        {
            FctCategory.DamageOut    => new(ParseColor(fs.ColorDamageOut),    ParseColor(fs.StrokeDamageOut),    fs.FontSizeDamageOut,    fs.FontSizeDamageOut    * 1.28, FontWeights.Normal, 2.2,  65),
            FctCategory.DamageIn     => new(ParseColor(fs.ColorDamageIn),     ParseColor(fs.StrokeDamageIn),     fs.FontSizeDamageIn,     fs.FontSizeDamageIn     * 1.28, FontWeights.Normal, 2.2, 300),
            FctCategory.CritOut      => new(ParseColor(fs.ColorCritOut),      ParseColor(fs.StrokeCritOut),      fs.FontSizeCritOut,      fs.FontSizeCritOut      * 2.0,  FontWeights.Bold,   3.0,  45),
            FctCategory.CritIn       => new(ParseColor(fs.ColorCritIn),       ParseColor(fs.StrokeCritIn),       fs.FontSizeCritIn,       fs.FontSizeCritIn       * 2.0,  FontWeights.Bold,   3.0, 315),
            FctCategory.SpellOut     => new(ParseColor(fs.ColorSpellOut),     ParseColor(fs.StrokeSpellOut),     fs.FontSizeSpellOut,     fs.FontSizeSpellOut     * 1.28, FontWeights.Normal, 2.2,  65),
            FctCategory.SpellIn      => new(ParseColor(fs.ColorSpellIn),      ParseColor(fs.StrokeSpellIn),      fs.FontSizeSpellIn,      fs.FontSizeSpellIn      * 1.28, FontWeights.Normal, 2.2, 300),
            FctCategory.HealFriendly => new(ParseColor(fs.ColorHealFriendly), ParseColor(fs.StrokeHealFriendly), fs.FontSizeHealFriendly, fs.FontSizeHealFriendly * 1.28, FontWeights.Normal, 2.2,  15),
            FctCategory.HealEnemy    => new(ParseColor(fs.ColorHealEnemy),    ParseColor(fs.StrokeHealEnemy),    fs.FontSizeHealEnemy,    fs.FontSizeHealEnemy    * 1.25, FontWeights.Normal, 2.2, 345),
            FctCategory.LevelUp      => new(ParseColor(fs.ColorLevelUp),      ParseColor(fs.StrokeLevelUp),      fs.FontSizeLevelUp,      fs.FontSizeLevelUp      * 1.47, FontWeights.Bold,   4.5,   0, Parabolic: true),
            FctCategory.ExpGain      => new(ParseColor(fs.ColorExpGain),      ParseColor(fs.StrokeExpGain),      fs.FontSizeExpGain,      fs.FontSizeExpGain      * 1.23, FontWeights.Normal,    2.0,   0, Parabolic: true),
            FctCategory.Stunned      => new(ParseColor(fs.ColorStunned),      ParseColor(fs.StrokeStunned),      fs.FontSizeStunned,      fs.FontSizeStunned      * 1.35, FontWeights.ExtraBold, 2.5,   0, Parabolic: true),
            _                        => new(Colors.White,                      Colors.Black,                      16,                      20,                             FontWeights.Normal, 2.2,   0),
        };
    }

    private static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.White; }
    }
}
