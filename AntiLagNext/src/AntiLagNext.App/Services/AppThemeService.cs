using System.Windows;
using System.Windows.Media;
using AntiLagNext.Core.Enums;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;

namespace AntiLagNext.App.Services;

/// <summary>
/// Applies dark/light/system theme to MaterialDesign + DesignSystem brushes.
/// Brand accent: DeepSkyBlue #00BFFF (not MD default cyan, not lime secondary).
/// </summary>
public static class AppThemeService
{
    public static readonly MediaColor DeepSkyBlue = MediaColor.FromRgb(0x00, 0xBF, 0xFF);
    public static readonly MediaColor DeepSkyBlueSoft = MediaColor.FromRgb(0x40, 0xD0, 0xFF);

    public static void Apply(AppTheme theme)
    {
        bool dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            AppTheme.System => IsSystemDarkTheme(),
            _ => true
        };

        try
        {
            var palette = new PaletteHelper();
            var md = palette.GetTheme();
            md.SetBaseTheme(dark ? BaseTheme.Dark : BaseTheme.Light);
            // Force brand color (overrides BundledTheme Cyan which is not #00BFFF)
            md.SetPrimaryColor(DeepSkyBlue);
            md.SetSecondaryColor(DeepSkyBlue); // never Lime
            palette.SetTheme(md);
        }
        catch
        {
            // MaterialDesign optional failure
        }

        var app = Application.Current;
        if (app?.Resources == null) return;

        if (dark)
            ApplyDarkBrushes(app.Resources);
        else
            ApplyLightBrushes(app.Resources);

        app.MainWindow?.InvalidateVisual();
    }

    private static void ApplyDarkBrushes(ResourceDictionary r)
    {
        SetBrush(r, "NavBackground", ColorFrom("#020203"));
        SetBrush(r, "ContentBackground", ColorFrom("#09090B"));
        SetBrush(r, "CardBackground", ColorFrom("#18181B"));
        SetBrush(r, "CardSoftBackground", ColorFrom("#0C0C0E"));
        SetBrush(r, "SurfaceBrush", ColorFrom("#18181B"));
        SetBrush(r, "BorderBrushSubtle", ColorFrom("#27272A"));
        SetBrush(r, "PrimaryText", ColorFrom("#FAFAFA"));
        SetBrush(r, "MutedText", ColorFrom("#A1A1AA"));
        SetBrush(r, "DimText", ColorFrom("#71717A"));
        SetBrush(r, "AccentBrush", DeepSkyBlue);
        SetBrush(r, "AccentCyanSoftBrush", DeepSkyBlueSoft);
        SetBrush(r, "AccentSecondaryBrush", ColorFrom("#38BDF8")); // cool sky, not lime
        SetBrush(r, "AccentVioletBrush", ColorFrom("#8B5CF6"));
        SetBrush(r, "SuccessBrush", ColorFrom("#34D399"));
        SetBrush(r, "WarningBrush", ColorFrom("#FBBF24"));
        SetBrush(r, "DangerBrush", ColorFrom("#F43F5E"));
        SetBrush(r, "OnAccentText", ColorFrom("#000000"));
        SetBrush(r, "OutlineButtonBackground", ColorFrom("#18181B"));
        SetBrush(r, "OutlineButtonForeground", ColorFrom("#E4E4E7"));
        SetBrush(r, "OutlineButtonHoverBackground", ColorFrom("#27272A"));
        SetBrush(r, "OutlineButtonHoverForeground", ColorFrom("#FAFAFA"));
        SetBrush(r, "NavHoverBackground", ColorFrom("#8027272A"));
        SetBrush(r, "NavHoverForeground", ColorFrom("#D4D4D8"));
        SetBrush(r, "PrimaryButtonBackground", DeepSkyBlue);
        SetBrush(r, "PrimaryButtonHoverBackground", DeepSkyBlueSoft);
        SetBrush(r, "GlassFillBrush", ColorFrom("#E618181B"), ColorFrom("#CC121214"));
        SetSolid(r, "GlassFillBrushSolid", ColorFrom("#E618181B"));
        var gb = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        gb.GradientStops.Add(new GradientStop(ColorFrom("#4027272A"), 0));
        gb.GradientStops.Add(new GradientStop(ColorFrom("#333F3F46"), 0.5));
        gb.GradientStops.Add(new GradientStop(ColorFrom("#1A00BFFF"), 1));
        gb.Freeze();
        r["GlassBorderBrush"] = gb;
    }

    private static void ApplyLightBrushes(ResourceDictionary r)
    {
        // Cool slate light theme — no warm zinc / milk / yellow tint
        SetBrush(r, "NavBackground", ColorFrom("#F1F5F9"));       // slate-100
        SetBrush(r, "ContentBackground", ColorFrom("#F8FAFC"));  // slate-50
        SetBrush(r, "CardBackground", ColorFrom("#FFFFFF"));
        SetBrush(r, "CardSoftBackground", ColorFrom("#F1F5F9"));
        SetBrush(r, "SurfaceBrush", ColorFrom("#FFFFFF"));
        SetBrush(r, "BorderBrushSubtle", ColorFrom("#E2E8F0"));  // slate-200
        SetBrush(r, "PrimaryText", ColorFrom("#0F172A"));        // slate-900
        SetBrush(r, "MutedText", ColorFrom("#475569"));          // slate-600
        SetBrush(r, "DimText", ColorFrom("#64748B"));            // slate-500
        SetBrush(r, "AccentBrush", DeepSkyBlue);
        SetBrush(r, "AccentCyanSoftBrush", DeepSkyBlueSoft);
        SetBrush(r, "AccentSecondaryBrush", ColorFrom("#0284C7")); // sky-600 cool
        SetBrush(r, "AccentVioletBrush", ColorFrom("#7C3AED"));
        SetBrush(r, "SuccessBrush", ColorFrom("#059669"));
        SetBrush(r, "WarningBrush", ColorFrom("#D97706"));
        SetBrush(r, "DangerBrush", ColorFrom("#E11D48"));
        SetBrush(r, "OnAccentText", ColorFrom("#000000"));
        SetBrush(r, "OutlineButtonBackground", ColorFrom("#FFFFFF"));
        SetBrush(r, "OutlineButtonForeground", ColorFrom("#0F172A"));
        SetBrush(r, "OutlineButtonHoverBackground", ColorFrom("#E2E8F0"));
        SetBrush(r, "OutlineButtonHoverForeground", ColorFrom("#020617"));
        SetBrush(r, "NavHoverBackground", ColorFrom("#E2E8F0"));
        SetBrush(r, "NavHoverForeground", ColorFrom("#0F172A"));
        SetBrush(r, "PrimaryButtonBackground", DeepSkyBlue);
        SetBrush(r, "PrimaryButtonHoverBackground", DeepSkyBlueSoft);
        SetSolid(r, "GlassFillBrushSolid", ColorFrom("#FFFFFFF5"));
        SetSolid(r, "GlassFillBrush", ColorFrom("#FFFFFF"));
        SetSolid(r, "GlassBorderBrush", ColorFrom("#E2E8F0"));
    }

    private static void SetBrush(ResourceDictionary r, string key, MediaColor c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        r[key] = b;
    }

    private static void SetBrush(ResourceDictionary r, string key, MediaColor c1, MediaColor c2)
    {
        var g = new LinearGradientBrush(c1, c2, 90);
        g.Freeze();
        r[key] = g;
    }

    private static void SetSolid(ResourceDictionary r, string key, MediaColor c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        r[key] = b;
    }

    private static MediaColor ColorFrom(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            return MediaColor.FromRgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
        if (hex.Length == 8)
        {
            return MediaColor.FromArgb(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16));
        }
        return Colors.Black;
    }

    public static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 0;
        }
        catch { /* ignore */ }
        return true;
    }
}
