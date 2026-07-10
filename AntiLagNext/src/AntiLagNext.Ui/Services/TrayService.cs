using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AntiLagNext.Ui.Services;

/// <summary>
/// WinForms NotifyIcon tray for Photino host. Hide/show via ShowWindow on native HWND.
/// Tray glyph: crop transparent padding + high-quality scale so the mark reads large.
/// </summary>
internal sealed class TrayService : IDisposable
{
    private NotifyIcon? _tray;
    private Icon? _iconOwned;
    private bool _disposed;

    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action? ApplyRequested;
    public event Action? ResetRequested;

    public bool IsVisible => _tray?.Visible == true;

    public void Init(Func<string, string, string> loc)
    {
        if (_tray != null) return;
        try
        {
            _iconOwned = LoadTrayIconLarge();
            _tray = new NotifyIcon
            {
                Icon = _iconOwned ?? SystemIcons.Application,
                Text = TruncTip(loc("AntiLag Next", "AntiLag Next")),
                Visible = true
            };
            _tray.DoubleClick += (_, _) => ShowRequested?.Invoke();
            RebuildMenu(loc);
        }
        catch
        {
            // tray is best-effort
        }
    }

    public void RebuildMenu(Func<string, string, string> loc)
    {
        if (_tray == null) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add(loc("Открыть", "Open"), null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add(loc("Включить оптимизацию", "Enable optimization"), null, (_, _) => ApplyRequested?.Invoke());
        menu.Items.Add(loc("Сбросить всё", "Reset all"), null, (_, _) => ResetRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(loc("Выход", "Exit"), null, (_, _) => ExitRequested?.Invoke());
        _tray.ContextMenuStrip = menu;
        _tray.Text = TruncTip(loc("AntiLag Next · в трее", "AntiLag Next · tray"));
    }

    public void ShowBalloon(string title, string text, int ms = 2500)
    {
        try
        {
            if (_tray == null) return;
            _tray.BalloonTipTitle = TruncTip(title, 63);
            _tray.BalloonTipText = text.Length > 255 ? text[..255] : text;
            _tray.ShowBalloonTip(ms);
        }
        catch { /* ignore */ }
    }

    public void SetVisible(bool visible)
    {
        if (_tray != null) _tray.Visible = visible;
    }

    public static void HideWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SwHide);
    }

    public static void ShowWindowRestore(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SwRestore);
        ShowWindow(hwnd, SwShow);
        SetForegroundWindow(hwnd);
    }

    /// <summary>
    /// Build a large, crisp tray icon: crop empty padding so the logo fills the cell.
    /// Windows tray slots are ~16–32 logical px; we feed 32–48 physical for HiDPI sharpness.
    /// </summary>
    private static Icon? LoadTrayIconLarge()
    {
        try
        {
            int size = ResolveTrayPixelSize(); // 32..48
            using var src = LoadSourceBitmap();
            if (src == null) return FallbackIco(size);

            using var cropped = CropTransparent(src);
            // Slight inset so edges aren't clipped by tray mask; keep glyph large
            using var scaled = ResizeHighQuality(cropped, size, size);
            return IconFromBitmap(scaled);
        }
        catch
        {
            return FallbackIco(32);
        }
    }

    private static int ResolveTrayPixelSize()
    {
        try
        {
            int sys = Math.Max(SystemInformation.SmallIconSize.Width, SystemInformation.SmallIconSize.Height);
            // Never smaller than 32; prefer 40–48 on HiDPI so mark looks bigger
            int dpiAware = Math.Max(sys, 32);
            // Bump one step for readability (Windows will downscale cleanly)
            return Math.Clamp(dpiAware >= 32 ? Math.Max(dpiAware, 40) : 32, 32, 48);
        }
        catch
        {
            return 40;
        }
    }

    private static Bitmap? LoadSourceBitmap()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "logo.png"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"),
            Path.Combine(AppContext.BaseDirectory, "app.ico"),
            Path.Combine(AppContext.BaseDirectory, "logo.png"),
        };
        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    using var ico = new Icon(path, 256, 256);
                    return ico.ToBitmap();
                }
                // FromFile locks path — copy to memory
                byte[] bytes = File.ReadAllBytes(path);
                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }
            catch { /* try next */ }
        }
        return null;
    }

    private static Icon? FallbackIco(int size)
    {
        try
        {
            string ico = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(ico))
                return new Icon(ico, size, size);
        }
        catch { /* ignore */ }
        return null;
    }

    /// <summary>Remove near-transparent padding so the glyph fills the tray cell (looks larger).</summary>
    private static Bitmap CropTransparent(Bitmap src)
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
            return (Bitmap)src.Clone();

        int pad = Math.Max(1, (int)((maxX - minX + 1) * 0.02));
        minX = Math.Max(0, minX - pad);
        minY = Math.Max(0, minY - pad);
        maxX = Math.Min(src.Width - 1, maxX + pad);
        maxY = Math.Min(src.Height - 1, maxY + pad);

        int w = maxX - minX + 1;
        int h = maxY - minY + 1;
        int side = Math.Max(w, h);
        var outBmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(outBmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        int ox = (side - w) / 2;
        int oy = (side - h) / 2;
        g.DrawImage(src, new Rectangle(ox, oy, w, h), minX, minY, w, h, GraphicsUnit.Pixel);
        return outBmp;
    }

    private static Bitmap ResizeHighQuality(Bitmap src, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        // Fill almost the full cell (slight 1px margin)
        int m = Math.Max(1, w / 16);
        g.DrawImage(src, new Rectangle(m, m, w - 2 * m, h - 2 * m));
        return bmp;
    }

    private static Icon IconFromBitmap(Bitmap bmp)
    {
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static string TruncTip(string s, int max = 63) =>
        s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            _iconOwned?.Dispose();
            _iconOwned = null;
        }
        catch { /* ignore */ }
    }

    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
