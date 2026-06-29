using System.Globalization;
using System.Windows.Media;

namespace GukVoice;

// FrameworkElement that renders text with a stroke (outline) around each glyph.
// TextBlock doesn't support stroke; we use FormattedText.BuildGeometry instead.
public sealed class OutlinedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedText),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(OutlinedText),
            new FrameworkPropertyMetadata(16.0,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(OutlinedText),
            new FrameworkPropertyMetadata(FontWeights.Normal,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(OutlinedText),
            new FrameworkPropertyMetadata(Brushes.White,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(OutlinedText),
            new FrameworkPropertyMetadata(Brushes.Black,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(OutlinedText),
            new FrameworkPropertyMetadata(1.5,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(OutlinedText),
            new FrameworkPropertyMetadata(FontStyles.Normal,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public string     Text            { get => (string)GetValue(TextProperty);            set => SetValue(TextProperty, value); }
    public double     FontSize        { get => (double)GetValue(FontSizeProperty);         set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight      { get => (FontWeight)GetValue(FontWeightProperty);   set => SetValue(FontWeightProperty, value); }
    public FontStyle  FontStyle       { get => (FontStyle)GetValue(FontStyleProperty);     set => SetValue(FontStyleProperty, value); }
    public Brush      Foreground      { get => (Brush)GetValue(ForegroundProperty);        set => SetValue(ForegroundProperty, value); }
    public Brush      StrokeBrush     { get => (Brush)GetValue(StrokeBrushProperty);       set => SetValue(StrokeBrushProperty, value); }
    public double     StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    private string _fontFamilyName = "Segoe UI";
    public string FontFamilyName
    {
        get => _fontFamilyName;
        set { _fontFamilyName = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    private FormattedText MakeFormattedText()
    {
        var src      = PresentationSource.FromVisual(this);
        double ppd   = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var family   = new FontFamily(_fontFamilyName);
        var typeface = new Typeface(family, FontStyle, FontWeight, FontStretches.Normal);
        return new FormattedText(
            Text ?? "",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            Foreground,
            ppd);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var ft    = MakeFormattedText();
        var thick = StrokeThickness;
        return new Size(ft.Width + thick, ft.Height + thick);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var ft  = MakeFormattedText();
        var off = StrokeThickness / 2.0;
        var geo = ft.BuildGeometry(new Point(off, off));
        if (StrokeThickness > 0 && StrokeBrush is { } sb)
            dc.DrawGeometry(null, new Pen(sb, StrokeThickness) { LineJoin = PenLineJoin.Round }, geo);
        dc.DrawGeometry(Foreground, null, geo);
    }
}
