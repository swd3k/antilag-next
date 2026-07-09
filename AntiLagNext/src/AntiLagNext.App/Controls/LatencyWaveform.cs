using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfRect = System.Windows.Shapes.Rectangle;

namespace AntiLagNext.App.Controls;

/// <summary>
/// Осциллограф latency в духе DPC Latency Checker:
/// столбики по времени + контур, цвет по порогам (зелёный / жёлтый / красный).
/// </summary>
public sealed class LatencyWaveform : Canvas
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values),
            typeof(IEnumerable<double>),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(
            nameof(MaxValue),
            typeof(double),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata(500.0, FrameworkPropertyMetadataOptions.AffectsRender, OnRedrawNeeded));

    public static readonly DependencyProperty GreenThresholdProperty =
        DependencyProperty.Register(
            nameof(GreenThreshold),
            typeof(double),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata(50.0, FrameworkPropertyMetadataOptions.AffectsRender, OnRedrawNeeded));

    public static readonly DependencyProperty YellowThresholdProperty =
        DependencyProperty.Register(
            nameof(YellowThreshold),
            typeof(double),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsRender, OnRedrawNeeded));

    public static readonly DependencyProperty UnitLabelProperty =
        DependencyProperty.Register(
            nameof(UnitLabel),
            typeof(string),
            typeof(LatencyWaveform),
            new FrameworkPropertyMetadata("µs", FrameworkPropertyMetadataOptions.AffectsRender, OnRedrawNeeded));

    private INotifyCollectionChanged? _subscribed;

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

    public LatencyWaveform()
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(MediaColor.FromRgb(0x09, 0x09, 0x0B));
        SizeChanged += (_, _) => Redraw();
        SnapsToDevicePixels = true;
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (LatencyWaveform)d;
        if (ctrl._subscribed != null)
            ctrl._subscribed.CollectionChanged -= ctrl.OnCollectionChanged;

        ctrl._subscribed = e.NewValue as INotifyCollectionChanged;
        if (ctrl._subscribed != null)
            ctrl._subscribed.CollectionChanged += ctrl.OnCollectionChanged;

        ctrl.Redraw();
    }

    private static void OnRedrawNeeded(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LatencyWaveform)d).Redraw();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(Redraw);

    private void Redraw()
    {
        Children.Clear();
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 10 || h < 10) return;

        var list = Values?.ToList() ?? new List<double>();
        double dataMax = list.Count > 0 ? list.Max() : 0;
        double scaleMax = Math.Max(MaxValue, Math.Max(dataMax * 1.15, GreenThreshold * 2));
        if (scaleMax < 1) scaleMax = 1;

        DrawGrid(w, h, scaleMax);

        if (list.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Нет данных — нажмите «Старт»",
                Foreground = new SolidColorBrush(MediaColor.FromRgb(0x6E, 0x6E, 0x8A)),
                FontSize = 13
            };
            Children.Add(empty);
            empty.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(empty, (w - empty.DesiredSize.Width) / 2);
            SetTop(empty, (h - empty.DesiredSize.Height) / 2);
            return;
        }

        int n = list.Count;
        double barW = Math.Max(1.0, w / n);
        var outline = new PointCollection();

        for (int i = 0; i < n; i++)
        {
            double v = Math.Max(0, list[i]);
            double bh = (v / scaleMax) * (h - 24);
            double x = i * barW;
            double y = h - 16 - bh;

            var rect = new WpfRect
            {
                Width = Math.Max(1, barW - 0.5),
                Height = Math.Max(1, bh),
                Fill = new SolidColorBrush(PickColor(v)),
                Opacity = 0.88
            };
            SetLeft(rect, x);
            SetTop(rect, y);
            Children.Add(rect);

            outline.Add(new WpfPoint(x + barW * 0.5, y));
        }

        if (outline.Count >= 2)
        {
            Children.Add(new Polyline
            {
                Points = outline,
                Stroke = new SolidColorBrush(MediaColor.FromRgb(0x06, 0xB6, 0xD4)),
                StrokeThickness = 2
            });
        }

        double last = list[^1];
        var badge = new TextBlock
        {
            Text = $"NOW {last:F0} {UnitLabel}",
            Foreground = new SolidColorBrush(PickColor(last)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            FontFamily = new MediaFontFamily("Consolas")
        };
        Children.Add(badge);
        badge.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(badge, w - badge.DesiredSize.Width - 10);
        SetTop(badge, 4);

        var legend = new TextBlock
        {
            Text = $"≤{GreenThreshold:F0} green · ≤{YellowThreshold:F0} yellow · > red  ({UnitLabel})",
            Foreground = new SolidColorBrush(MediaColor.FromRgb(0x8A, 0x8A, 0xA8)),
            FontSize = 10
        };
        SetLeft(legend, 48);
        SetTop(legend, 4);
        Children.Add(legend);
    }

    private void DrawGrid(double w, double h, double scaleMax)
    {
        var gridBrush = new SolidColorBrush(MediaColor.FromRgb(0x2A, 0x2A, 0x3A));
        var labelBrush = new SolidColorBrush(MediaColor.FromRgb(0x7A, 0x7A, 0x96));

        for (int i = 0; i <= 4; i++)
        {
            double frac = i / 4.0;
            double y = h - 16 - frac * (h - 24);
            Children.Add(new Line
            {
                X1 = 0, X2 = w, Y1 = y, Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1
            });

            var tb = new TextBlock
            {
                Text = $"{scaleMax * frac:F0}",
                Foreground = labelBrush,
                FontSize = 10,
                FontFamily = new MediaFontFamily("Consolas")
            };
            SetLeft(tb, 4);
            SetTop(tb, Math.Max(0, y - 12));
            Children.Add(tb);
        }

        void ThresholdLine(double us, MediaColor c)
        {
            double y = h - 16 - (us / scaleMax) * (h - 24);
            if (y < 0 || y > h) return;
            Children.Add(new Line
            {
                X1 = 40, X2 = w - 4, Y1 = y, Y2 = y,
                Stroke = new SolidColorBrush(c) { Opacity = 0.55 },
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            });
        }

        ThresholdLine(GreenThreshold, MediaColor.FromRgb(0x4C, 0xAF, 0x50));
        ThresholdLine(YellowThreshold, MediaColor.FromRgb(0xFF, 0xC1, 0x07));
    }

    private MediaColor PickColor(double v)
    {
        if (v <= GreenThreshold) return MediaColor.FromRgb(0x4C, 0xAF, 0x50);
        if (v <= YellowThreshold) return MediaColor.FromRgb(0xFF, 0xC1, 0x07);
        return MediaColor.FromRgb(0xF4, 0x43, 0x36);
    }
}
