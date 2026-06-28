using System.Runtime.InteropServices;
using System.Windows.Controls;
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

    private const double TravelDistance = 140.0;  // pixels travelled along the angle

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

            // Centre of EQ window — small scatter perpendicular to travel direction
            double cx      = OverlayCanvas.ActualWidth  / 2.0;
            double cy      = OverlayCanvas.ActualHeight / 2.0;
            double scatter = _rng.Next(-20, 20);

            // Angle: 0° = up, 90° = right, clockwise
            double rad = style.AngleDeg * Math.PI / 180.0;
            double dx  = Math.Sin(rad);   // screen X component
            double dy  = -Math.Cos(rad);  // screen Y component (negative = up)

            // Perpendicular axis for scatter so it's always sideways to travel
            double perpDx = -dy;
            double perpDy = dx;

            double startX = cx + scatter * perpDx - textW / 2.0;
            double startY = cy + scatter * perpDy - textH / 2.0;

            Canvas.SetLeft(tb, startX);
            Canvas.SetTop(tb, startY);
            OverlayCanvas.Children.Add(tb);

            var dur    = TimeSpan.FromSeconds(style.Duration);
            var halfDur = TimeSpan.FromSeconds(style.Duration * 0.5);
            var sb     = new Storyboard();

            if (style.Parabolic)
            {
                // Exp / Level Up: arc up then fall back down
                var yAnim = new DoubleAnimationUsingKeyFrames();
                yAnim.KeyFrames.Add(new LinearDoubleKeyFrame(startY,        KeyTime.FromPercent(0.0)));
                yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(startY - 90,   KeyTime.FromPercent(0.4))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                yAnim.KeyFrames.Add(new EasingDoubleKeyFrame(startY + 60,   KeyTime.FromPercent(1.0))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                Storyboard.SetTarget(yAnim, tb);
                Storyboard.SetTargetProperty(yAnim, new PropertyPath(Canvas.TopProperty));
                sb.Children.Add(yAnim);
            }
            else
            {
                // Directional travel along the specified angle
                var xAnim = new DoubleAnimation(startX, startX + dx * TravelDistance, dur)
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(xAnim, tb);
                Storyboard.SetTargetProperty(xAnim, new PropertyPath(Canvas.LeftProperty));
                sb.Children.Add(xAnim);

                var yAnim = new DoubleAnimation(startY, startY + dy * TravelDistance, dur)
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(yAnim, tb);
                Storyboard.SetTargetProperty(yAnim, new PropertyPath(Canvas.TopProperty));
                sb.Children.Add(yAnim);
            }

            // Font size grows during the animation
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
    // AngleDeg: 0=up, 90=right, 180=down, 270=left — clockwise from top
    // StartSize → EndSize: font grows during the animation
    // Parabolic: arcs up then falls down (used for level/exp)

    private record StyleDef(
        Color Color, double StartSize, double EndSize,
        FontWeight Weight, double Duration, double AngleDeg, bool Parabolic = false);

    private static StyleDef GetStyle(FctCategory cat) => cat switch
    {
        FctCategory.DamageOut    => new(Color.FromRgb(0xFF, 0xFF, 0xFF), 18, 23, FontWeights.Normal, 1.4,  65),
        FctCategory.DamageIn     => new(Color.FromRgb(0xFF, 0x70, 0x43), 18, 23, FontWeights.Normal, 1.4, 300),
        FctCategory.CritOut      => new(Color.FromRgb(0xFF, 0xD7, 0x00), 26, 52, FontWeights.Bold,   2.0,  45),
        FctCategory.CritIn       => new(Color.FromRgb(0xFF, 0x30, 0x30), 26, 52, FontWeights.Bold,   2.0, 315),
        FctCategory.SpellOut     => new(Color.FromRgb(0x64, 0xB5, 0xF6), 18, 23, FontWeights.Normal, 1.4,  65),
        FctCategory.SpellIn      => new(Color.FromRgb(0xCE, 0x93, 0xD8), 18, 23, FontWeights.Normal, 1.4, 300),
        FctCategory.HealFriendly => new(Color.FromRgb(0x81, 0xC7, 0x84), 18, 23, FontWeights.Normal, 1.4,  15),
        FctCategory.HealEnemy    => new(Color.FromRgb(0xCD, 0xDC, 0x39), 16, 20, FontWeights.Normal, 1.4, 345),
        FctCategory.LevelUp      => new(Color.FromRgb(0xFF, 0xD7, 0x00), 30, 44, FontWeights.Bold,   3.0,   0, Parabolic: true),
        FctCategory.ExpGain      => new(Color.FromRgb(0xFF, 0xF5, 0x9D), 13, 16, FontWeights.Normal, 1.2,   0, Parabolic: true),
        _                        => new(Colors.White,                     16, 20, FontWeights.Normal, 1.4,   0),
    };
}
