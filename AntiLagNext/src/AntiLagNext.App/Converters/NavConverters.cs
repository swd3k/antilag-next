using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace AntiLagNext.App.Converters;

public sealed class EqualityToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>Inverts a bool (for IsEnabled when IsBusy, etc.).</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>Visible when collection Count is 0 (empty state). Invert shows when non-empty.</summary>
public sealed class CountZeroToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int count = value is int i ? i : 0;
        bool empty = count == 0;
        bool show = Invert ? !empty : empty;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

/// <summary>Active nav: zinc fill + optional Tag "rail" via converter parameter use in MultiBinding not needed —
/// returns solid with Opacity 0.8; transparent when inactive.</summary>
public sealed class NavActiveBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool active = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        if (!active) return MediaBrushes.Transparent;
        // Theme-aware active chip: slightly tinted surface
        if (Application.Current?.Resources["BorderBrushSubtle"] is SolidColorBrush border)
        {
            var c = border.Color;
            return CreateFrozen(MediaColor.FromArgb(0x99, c.R, c.G, c.B));
        }
        return CreateFrozen(MediaColor.FromArgb(0xCC, 0x27, 0x27, 0x2A));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;

    private static SolidColorBrush CreateFrozen(MediaColor c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>Cyan left rail opacity: 1 when active, 0 when not.</summary>
public sealed class NavActiveRailOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool active = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        return active ? 1.0 : 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

public sealed class NavActiveBorderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => MediaBrushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

public sealed class NavActiveForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool active = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        var res = Application.Current?.Resources;
        if (active)
        {
            if (res?["PrimaryText"] is SolidColorBrush p) return p;
            return new SolidColorBrush(MediaColor.FromRgb(0xD4, 0xD4, 0xD8));
        }
        if (res?["MutedText"] is SolidColorBrush m) return m;
        return new SolidColorBrush(MediaColor.FromRgb(0xA1, 0xA1, 0xAA));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool active = value is true;
        return active
            ? new SolidColorBrush(MediaColor.FromRgb(0x34, 0xD3, 0x99))
            : new SolidColorBrush(MediaColor.FromRgb(0x71, 0x71, 0x7A));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

public sealed class StringNullOrEmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool empty = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
        bool show = Invert ? empty : !empty;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

/// <summary>Visible only for ProfileKind.Custom (row delete icon).</summary>
public sealed class ProfileCustomVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AntiLagNext.Core.Enums.ProfileKind kind)
            return kind == AntiLagNext.Core.Enums.ProfileKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        if (value is int i)
            return i == (int)AntiLagNext.Core.Enums.ProfileKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
