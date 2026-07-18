namespace AntiLagNext.Core.Models;

/// <summary>Minimal SemVer (major.minor.patch) for update checks.</summary>
public readonly struct SemVer : IComparable<SemVer>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public SemVer(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public static bool TryParse(string? text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];
        // strip pre-release / build metadata
        int cut = s.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) s = s[..cut];
        var parts = s.Split('.');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out int maj)) return false;
        if (!int.TryParse(parts[1], out int min)) return false;
        int pat = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out pat)) return false;
        if (maj < 0 || min < 0 || pat < 0) return false;
        version = new SemVer(maj, min, pat);
        return true;
    }

    public int CompareTo(SemVer other)
    {
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        return Patch.CompareTo(other.Patch);
    }

    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>Result of checking GitHub Releases for a newer Setup.</summary>
public sealed class UpdateCheckResult
{
    public required string LocalVersion { get; init; }
    public string? LatestVersion { get; init; }
    public bool HasUpdate { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ReleaseUrl { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? AssetName { get; init; }
    public bool CanSilentInstall { get; init; }
    /// <summary>English fallback message (never OS-locale Win32 text).</summary>
    public string? Error { get; init; }
    /// <summary>Stable code for UI i18n: network, timeout, http, parse, unknown.</summary>
    public string? ErrorCode { get; init; }
    public bool IsPortable { get; init; }
}
