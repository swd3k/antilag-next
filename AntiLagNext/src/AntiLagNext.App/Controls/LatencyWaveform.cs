using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfRect = System.Windows.Rect;
using WpfFlowDirection = System.Windows.FlowDirection;

namespace AntiLagNext.App.Controls;

/// <summary>
/// Latency waveform — DrawingVisual/OnRender (без Shape-дерева каждый тик = меньше self-noise).
/// Theme-aware colors via Application.Current.Resources with dark fallbacks.
/// </summary>
public sealed class LatencyWaveform : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values),
            typeof(IEnumerable<double>),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(
            nameof(MaxValue),
            typeof(double),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata(500.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GreenThresholdProperty =
        DependencyProperty.Register(
            nameof(GreenThreshold),
            typeof(double),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata(50.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YellowThresholdProperty =
        DependencyProperty.Register(
            nameof(YellowThreshold),
            typeof(double),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitLabelProperty =
        DependencyProperty.Register(
            nameof(UnitLabel),
            typeof(string),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata("µs", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(
            nameof(EmptyText),
            typeof(string),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata("Waiting for samples…", FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _subscribed;
    private List<double> _cache = new();
    private bool _redrawQueued;

    private static readonly MediaFontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");
    private static readonly Typeface LabelTypeface = new(MonoFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldTypeface = new(MonoFont, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    // Dark fallbacks when resources unavailable
    private static readonly MediaColor FallbackBg = MediaColor.FromRgb(0x09, 0x09, 0x0B);
    private static readonly MediaColor FallbackDim = MediaColor.FromRgb(0x71, 0x71, 0x7A);
    private static readonly MediaColor FallbackMuted = MediaColor.FromRgb(0xA1, 0xA1, 0xAA);
    private static readonly MediaColor FallbackAccent = MediaColor.FromRgb(0x06, 0xB6, 0xD4);
    private static readonly MediaColor FallbackGrid = MediaColor.FromRgb(0x27, 0x27, 0x2A);

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public double GreenThreshold
    {
        get => (double)GetValue(GreenThresholdProperty);
        set => SetValue(GreenThresholdProperty, value);
    }

    public double YellowThreshold
    {
        get => (double)GetValue(YellowThresholdProperty);
        set => SetValue(YellowThresholdProperty, value);
    }

    public string UnitLabel
    {
        get => (string)GetValue(UnitLabelProperty);
        set => SetValue(UnitLabelProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public LatencyWaveform()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        MinHeight = 160;
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (LatencyWaveform)d;
        if (ctrl._subscribed != null)
            ctrl._subscribed.CollectionChanged -= ctrl.OnCollectionChanged;

        ctrl._subscribed = e.NewValue as INotifyCollectionChanged;
        if (ctrl._subscribed != null)
            ctrl._subscribed.CollectionChanged += ctrl.OnCollectionChanged;

        ctrl.QueueRedraw();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRedraw();

    private void QueueRedraw()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _redrawQueued = false;
            if (Values is IList<double> list)
                _cache = list.ToList();
            else
                _cache = Values?.ToList() ?? new List<double>();
            InvalidateVisual();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 10 || h < 10) return;

        var bgColor = ResolveColor("ContentBackground", FallbackBg);
        var bg = new SolidColorBrush(bgColor);
        bg.Freeze();
        dc.DrawRectangle(bg, null, new WpfRect(0, 0, w, h));

        var dim = ResolveColor("DimText", FallbackDim);
        var muted = ResolveColor("MutedText", FallbackMuted);
        var accent = ResolveColor("AccentBrush", FallbackAccent);

        var list = _cache;
        double dataMax = list.Count > 0 ? list.Max() : 0;
        double scaleMax = Math.Max(MaxValue, Math.Max(dataMax * 1.15, GreenThreshold * 2));
        if (scaleMax < 1) scaleMax = 1;

        const double padL = 40, padR = 12, padT = 28, padB = 18;
        double plotW = Math.Max(1, w - padL - padR);
        double plotH = Math.Max(1, h - padT - padB);

        DrawGrid(dc, padL, padT, plotW, plotH, scaleMax, dim);

        if (list.Count == 0)
        {
            var empty = string.IsNullOrWhiteSpace(EmptyText) ? "Waiting for samples…" : EmptyText;
            DrawText(dc, empty,
                w * 0.5, h * 0.45, 13, muted, center: true);
            DrawText(dc, "Analytics → Start",
                w * 0.5, h * 0.45 + 22, 11, dim, center: true);
            return;
        }

        int n = list.Count;
        double barW = Math.Max(1.0, plotW / n);
        var outline = new StreamGeometry();
        using (var ctx = outline.Open())
        {
            bool started = false;
            for (int i = 0; i < n; i++)
            {
                double v = Math.Max(0, list[i]);
                double bh = (v / scaleMax) * plotH;
                double x = padL + i * barW;
                double y = padT + plotH - bh;

                var fill = new SolidColorBrush(PickColor(v)) { Opacity = 0.85 };
                fill.Freeze();
                dc.DrawRectangle(fill, null, new WpfRect(x, y, Math.Max(1, barW - 0.6), Math.Max(1, bh)));

                double midX = x + barW * 0.5;
                if (!started)
                {
                    ctx.BeginFigure(new WpfPoint(midX, y), false, false);
                    started = true;
                }
                else
                {
                    ctx.LineTo(new WpfPoint(midX, y), true, false);
                }
            }
        }
        outline.Freeze();
        var stroke = new SolidColorBrush(accent);
        stroke.Freeze();
        dc.DrawGeometry(null, new MediaPen(stroke, 1.5), outline);

        double last = list[^1];
        DrawText(dc, $"≤{GreenThreshold:F0} green · ≤{YellowThreshold:F0} yellow · > red",
            padL, 8, 10, dim, center: false);
        DrawText(dc, $"NOW {last:F0} {UnitLabel}",
            w - padR, 8, 12, PickColor(last), center: false, rightAlign: true, bold: true);
    }

    private void DrawGrid(DrawingContext dc, double padL, double padT, double plotW, double plotH, double scaleMax, MediaColor labelColor)
    {
        var gridColor = ResolveColor("BorderBrushSubtle", FallbackGrid);
        // Soften grid for light backgrounds
        var gridBrush = new SolidColorBrush(gridColor) { Opacity = 0.55 };
        gridBrush.Freeze();
        var gridPen = new MediaPen(gridBrush, 1);
        gridPen.Freeze();

        for (int i = 0; i <= 4; i++)
        {
            double frac = i / 4.0;
            double y = padT + plotH - frac * plotH;
            dc.DrawLine(gridPen, new WpfPoint(padL, y), new WpfPoint(padL + plotW, y));
            DrawText(dc, $"{scaleMax * frac:F0}", 4, y - 8, 10, labelColor, center: false);
        }

        void ThresholdLine(double us, MediaColor c)
        {
            double y = padT + plotH - (us / scaleMax) * plotH;
            if (y < padT || y > padT + plotH) return;
            var pen = new MediaPen(new SolidColorBrush(c) { Opacity = 0.45 }, 1)
            {
                DashStyle = DashStyles.Dash
            };
            pen.Freeze();
            dc.DrawLine(pen, new WpfPoint(padL, y), new WpfPoint(padL + plotW, y));
        }

        ThresholdLine(GreenThreshold, MediaColor.FromRgb(0x34, 0xD3, 0x99));
        ThresholdLine(YellowThreshold, MediaColor.FromRgb(0xFB, 0xBF, 0x24));
    }

    private static void DrawText(
        DrawingContext dc, string text, double x, double y, double size,
        MediaColor color, bool center, bool rightAlign = false, bool bold = false)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            WpfFlowDirection.LeftToRight,
            bold ? BoldTypeface : LabelTypeface,
            size,
            brush,
            1.25);

        double dx = x;
        if (center) dx = x - ft.Width / 2;
        else if (rightAlign) dx = x - ft.Width;
        dc.DrawText(ft, new WpfPoint(dx, y));
    }

    private MediaColor PickColor(double v)
    {
        if (v <= GreenThreshold) return MediaColor.FromRgb(0x34, 0xD3, 0x99);
        if (v <= YellowThreshold) return MediaColor.FromRgb(0xFB, 0xBF, 0x24);
        return MediaColor.FromRgb(0xF4, 0x3F, 0x5E);
    }

    private static MediaColor ResolveColor(string resourceKey, MediaColor fallback)
    {
        try
        {
            var app = Application.Current;
            if (app?.Resources == null) return fallback;

            if (app.Resources[resourceKey] is SolidColorBrush scb)
                return scb.Color;

            if (app.TryFindResource(resourceKey) is SolidColorBrush found)
                return found.Color;
        }
        catch
        {
            // design-time / early init
        }

        return fallback;
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? MinHeight : Math.Max(MinHeight, availableSize.Height);
        return new WpfSize(w, h);
    }
}
