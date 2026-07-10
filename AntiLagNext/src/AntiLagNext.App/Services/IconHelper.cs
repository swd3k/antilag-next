using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;
using DrawingSize = System.Drawing.Size;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingInterpolationMode = System.Drawing.Drawing2D.InterpolationMode;
using DrawingSmoothingMode = System.Drawing.Drawing2D.SmoothingMode;
using DrawingPixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode;
using DrawingCompositingQuality = System.Drawing.Drawing2D.CompositingQuality;

namespace AntiLagNext.App.Services;

/// <summary>Load multi-size app icons for window/tray with high-quality downscale + crop.</summary>
public static class IconHelper
{
    public const string PackIco = "pack://application:,,,/Assets/app.ico";
    public const string PackPng = "pack://application:,,,/Assets/logo.png";

    /// <summary>
    /// Tray icons: use system small-icon size but never below 32 so the mark stays readable on HiDPI.
    /// Source art is cropped (transparent padding removed) before scale.
    /// </summary>
    public static DrawingIcon LoadTrayIcon(int? preferredSize = null)
    {
        int size = preferredSize ?? ResolveTrayPixelSize();
        size = Math.Clamp(size, 16, 64);
        try
        {
            using var srcBmp = LoadSourceBitmap();
            if (srcBmp != null)
            {
                using var cropped = CropTransparent(srcBmp);
                using var scaled = ResizeBitmap(cropped, size, size);
                return IconFromBitmap(scaled);
            }
        }
        catch
        {
            // fall through
        }

        try
        {
            var streamInfo = Application.GetResourceStream(new Uri(PackIco));
            if (streamInfo != null)
            {
                using var s = streamInfo.Stream;
                using var ico = new DrawingIcon(s, new DrawingSize(size, size));
                return (DrawingIcon)ico.Clone();
            }
        }
        catch { /* ignore */ }

        return (DrawingIcon)SystemIcons.Application.Clone();
    }

    public static int ResolveTrayPixelSize()
    {
        try
        {
            // SmallIconSize is DPI-aware (16 @100%, 32 @200%, etc.)
            int sys = Math.Max(
                System.Windows.Forms.SystemInformation.SmallIconSize.Width,
                System.Windows.Forms.SystemInformation.SmallIconSize.Height);
            // Prefer a slightly larger mark when OS reports 16 only
            return Math.Max(sys, 32);
        }
        catch
        {
            return 32;
        }
    }

    public static BitmapImage? LoadLogoBitmapImage()
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(PackPng, UriKind.Absolute);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    private static DrawingBitmap? LoadSourceBitmap()
    {
        try
        {
            var png = Application.GetResourceStream(new Uri(PackPng));
            if (png != null)
            {
                using var s = png.Stream;
                return new DrawingBitmap(s);
            }
        }
        catch { /* try ico */ }

        try
        {
            var ico = Application.GetResourceStream(new Uri(PackIco));
            if (ico != null)
            {
                using var s = ico.Stream;
                using var icon = new DrawingIcon(s, new DrawingSize(256, 256));
                return icon.ToBitmap();
            }
        }
        catch { /* ignore */ }

        return null;
    }

    /// <summary>Remove near-transparent padding so the glyph fills the tray cell.</summary>
    private static DrawingBitmap CropTransparent(DrawingBitmap src)
    {
        int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < src.Height; y++)
        {
            for (int x = 0; x < src.Width; x++)
            {
                if (src.GetPixel(x, y).A > 16)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0)
            return (DrawingBitmap)src.Clone();

        int pad = Math.Max(1, (int)((maxX - minX + 1) * 0.04));
        minX = Math.Max(0, minX - pad);
        minY = Math.Max(0, minY - pad);
        maxX = Math.Min(src.Width - 1, maxX + pad);
        maxY = Math.Min(src.Height - 1, maxY + pad);

        int w = maxX - minX + 1;
        int h = maxY - minY + 1;
        int side = Math.Max(w, h);
        var outBmp = new DrawingBitmap(side, side);
        using var g = DrawingGraphics.FromImage(outBmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = DrawingInterpolationMode.HighQualityBicubic;
        g.SmoothingMode = DrawingSmoothingMode.HighQuality;
        g.PixelOffsetMode = DrawingPixelOffsetMode.HighQuality;
        int ox = (side - w) / 2;
        int oy = (side - h) / 2;
        g.DrawImage(src, new Rectangle(ox, oy, w, h), minX, minY, w, h, GraphicsUnit.Pixel);
        return outBmp;
    }

    private static DrawingBitmap ResizeBitmap(DrawingBitmap src, int w, int h)
    {
        var bmp = new DrawingBitmap(w, h);
        using var g = DrawingGraphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = DrawingInterpolationMode.HighQualityBicubic;
        g.SmoothingMode = DrawingSmoothingMode.HighQuality;
        g.PixelOffsetMode = DrawingPixelOffsetMode.HighQuality;
        g.CompositingQuality = DrawingCompositingQuality.HighQuality;
        g.DrawImage(src, 0, 0, w, h);
        return bmp;
    }

    private static DrawingIcon IconFromBitmap(DrawingBitmap bmp)
    {
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = DrawingIcon.FromHandle(hIcon);
            return (DrawingIcon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
