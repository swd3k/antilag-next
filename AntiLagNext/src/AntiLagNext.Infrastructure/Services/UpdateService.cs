using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;

namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// GitHub Releases updater: check latest SemVer, download Setup, silent Inno install.
/// Primary: api.github.com. Fallback: github.com Atom + /releases/latest (many networks block the API).
/// </summary>
public sealed class UpdateService : IUpdateService
{
    public const string Owner = "swd3k";
    public const string Repo = "antilag-next";
    public const string ReleasesApi = "https://api.github.com/repos/swd3k/antilag-next/releases/latest";
    public const string ReleasesPage = "https://github.com/swd3k/antilag-next/releases/latest";
    public const string ReleasesAtom = "https://github.com/swd3k/antilag-next/releases.atom";
    public const string ReleasesLatestRedirect = "https://github.com/swd3k/antilag-next/releases/latest";

    /// <summary>Stable error codes for UI i18n (never OS-locale exception text).</summary>
    public static class ErrorCodes
    {
        public const string Network = "network";
        public const string Timeout = "timeout";
        public const string Http = "http";
        public const string Parse = "parse";
        public const string Unknown = "unknown";
    }

    private static readonly HttpClient Http = CreateClient();

    // First /releases/tag/vX.Y.Z or /releases/download/vX.Y.Z or atom id …/vX.Y.Z
    private static readonly Regex TagInUrl = new(
        @"/releases/(?:tag|download)/(v?\d+\.\d+\.\d+(?:[.-][A-Za-z0-9.]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagInAtomId = new(
        @"/v?\d+\.\d+\.\d+(?:[.-][A-Za-z0-9.]+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string LocalVersion { get; } = ReadLocalVersion();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        string local = LocalVersion;
        Exception? lastFail = null;

        // 1) GitHub REST API (rich: assets list) — short budget; often blocked on some ISPs
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var api = await TryCheckViaApiAsync(local, cts.Token).ConfigureAwait(false);
            if (api is not null)
                return api;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested)
        {
            lastFail = ex;
            if (cancellationToken.IsCancellationRequested)
                throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            lastFail = new TimeoutException("GitHub API timed out");
        }

        // 2) Atom feed on github.com (works when api.github.com is unreachable)
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var atom = await TryCheckViaAtomAsync(local, cts.Token).ConfigureAwait(false);
            if (atom is not null)
                return atom;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested)
        {
            lastFail = ex;
            if (cancellationToken.IsCancellationRequested)
                throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            lastFail = new TimeoutException("GitHub Atom timed out");
        }

        // 3) Follow /releases/latest redirect → tag URL
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var redir = await TryCheckViaLatestRedirectAsync(local, cts.Token).ConfigureAwait(false);
            if (redir is not null)
                return redir;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested)
        {
            lastFail = ex;
            if (cancellationToken.IsCancellationRequested)
                throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            lastFail = new TimeoutException("GitHub latest redirect timed out");
        }

        var (code, msg) = ClassifyError(lastFail);
        return new UpdateCheckResult
        {
            LocalVersion = local,
            Error = msg,
            ErrorCode = code,
            ReleaseUrl = ReleasesPage,
            IsPortable = IsPortableInstall()
        };
    }

    public async Task<OperationResult> DownloadAndInstallAsync(
        UpdateCheckResult check,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (check is null || !check.HasUpdate)
            return OperationResult.Ok("Already up to date.");

        if (string.IsNullOrWhiteSpace(check.DownloadUrl))
            return OperationResult.Fail(
                "No Setup asset for this architecture.",
                detail: check.ReleaseUrl);

        if (check.IsPortable || !check.CanSilentInstall)
            return OperationResult.Fail(
                "Silent update requires Program Files install. Open Releases to download Setup.",
                detail: check.ReleaseUrl);

        if (!IsAllowedDownloadUrl(check.DownloadUrl))
            return OperationResult.Fail("Download URL rejected (not official repo).");

        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "AntiLagNext-update");
            Directory.CreateDirectory(dir);
            string fileName = check.AssetName ?? "AntiLagNext-Setup-update.exe";
            foreach (var c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');
            string dest = Path.Combine(dir, fileName);

            using var resp = await Http.GetAsync(
                    check.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            long? total = resp.Content.Headers.ContentLength;
            await using (var input = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    readTotal += read;
                    if (total is > 0)
                        progress?.Report(Math.Clamp(readTotal / (double)total.Value, 0, 1));
                }
            }

            progress?.Report(1);

            string args =
                "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS";
            var psi = new ProcessStartInfo
            {
                FileName = dest,
                Arguments = args,
                UseShellExecute = true,
            };
            Process.Start(psi);

            await Task.Delay(800, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok("Updater started; application will exit.");
        }
        catch (Exception ex)
        {
            var (code, msg) = ClassifyError(ex);
            return OperationResult.Fail(msg, detail: code + ": " + ex.GetType().Name, ex: ex);
        }
    }

    public static string GetCurrentRid()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return "win-arm64";
        if (RuntimeInformation.ProcessArchitecture == Architecture.X86)
            return "win-x86";
        return "win-x64";
    }

    /// <summary>
    /// True if portable zip (not Inno install under Program Files / uninstall registry).
    /// </summary>
    public static bool IsPortableInstall()
    {
        try
        {
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "AntiLagNext.portable")))
                return true;

            string baseDir = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf)
                && baseDir.StartsWith(pf, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(pf86)
                && baseDir.StartsWith(pf86, StringComparison.OrdinalIgnoreCase))
                return false;

            if (HasUninstallEntry())
                return false;

            return true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Build canonical Setup download URL for a release tag/version.</summary>
    public static string BuildSetupDownloadUrl(string version, string rid)
    {
        string ver = version.Trim().TrimStart('v', 'V');
        return $"https://github.com/{Owner}/{Repo}/releases/download/v{ver}/AntiLagNext-Setup-{ver}-{rid}.exe";
    }

    public static string BuildSetupAssetName(string version, string rid)
    {
        string ver = version.Trim().TrimStart('v', 'V');
        return $"AntiLagNext-Setup-{ver}-{rid}.exe";
    }

    /// <summary>Extract tag/version from Atom XML or redirect Location (unit-testable).</summary>
    public static bool TryParseLatestTagFromAtom(string atomXml, out string tag)
    {
        tag = "";
        if (string.IsNullOrWhiteSpace(atomXml)) return false;

        // Prefer first <entry>…</entry> block
        int entryStart = atomXml.IndexOf("<entry", StringComparison.OrdinalIgnoreCase);
        string slice = entryStart >= 0 ? atomXml[entryStart..] : atomXml;

        // <link rel="alternate" ... href=".../releases/tag/v1.2.0"/>
        var linkMatch = Regex.Match(
            slice,
            @"href\s*=\s*""([^""]*/releases/tag/[^""]+)""",
            RegexOptions.IgnoreCase);
        if (linkMatch.Success && TryExtractTag(linkMatch.Groups[1].Value, out tag))
            return true;

        // <id>tag:github.com,2008:Repository/…/v1.2.0</id>
        var idMatch = Regex.Match(
            slice,
            @"<id>\s*([^<]+)\s*</id>",
            RegexOptions.IgnoreCase);
        if (idMatch.Success && TryExtractTag(idMatch.Groups[1].Value.Trim(), out tag))
            return true;

        // title: AntiLag Next v1.2.0
        var titleMatch = Regex.Match(
            slice,
            @"<title>\s*([^<]*v?\d+\.\d+\.\d+[^<]*)\s*</title>",
            RegexOptions.IgnoreCase);
        if (titleMatch.Success)
        {
            var verInTitle = Regex.Match(titleMatch.Groups[1].Value, @"v?\d+\.\d+\.\d+");
            if (verInTitle.Success)
            {
                tag = verInTitle.Value;
                return true;
            }
        }

        return false;
    }

    public static bool TryExtractTag(string? text, out string tag)
    {
        tag = "";
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = TagInUrl.Match(text);
        if (m.Success)
        {
            tag = m.Groups[1].Value;
            return true;
        }

        // bare id ending with /v1.2.0
        m = TagInAtomId.Match(text.Trim());
        if (m.Success)
        {
            tag = m.Value.TrimStart('/');
            return true;
        }

        // plain v1.2.0 or 1.2.0
        if (SemVer.TryParse(text, out _))
        {
            tag = text.Trim();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Map exceptions to stable English messages + codes (never OS-locale Win32 text).
    /// </summary>
    public static (string Code, string Message) ClassifyError(Exception? ex)
    {
        if (ex is null)
            return (ErrorCodes.Network, "Could not reach GitHub. Check network or open Releases manually.");

        // Unwrap
        Exception root = ex;
        while (root.InnerException is not null
               && root is HttpRequestException or TaskCanceledException or AggregateException)
            root = root.InnerException;

        if (ex is TaskCanceledException or OperationCanceledException or TimeoutException
            || root is TimeoutException or TaskCanceledException)
            return (ErrorCodes.Timeout, "Update check timed out. Try again or open Releases.");

        if (root is SocketException or IOException
            || ex is HttpRequestException
            || root is HttpRequestException)
            return (ErrorCodes.Network, "Could not reach GitHub. Check network or open Releases manually.");

        return (ErrorCodes.Unknown, "Update check failed. Open Releases for the latest Setup.");
    }

    private async Task<UpdateCheckResult?> TryCheckViaApiAsync(string local, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null; // try fallbacks

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            return UpToDate(local, ReleasesPage);

        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
            return UpToDate(local, ReleasesPage);

        string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        string? htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : ReleasesPage;
        string? body = root.TryGetProperty("body", out var b) ? b.GetString() : null;

        if (!SemVer.TryParse(tag, out var latest) || !SemVer.TryParse(local, out var localSem))
        {
            return new UpdateCheckResult
            {
                LocalVersion = local,
                LatestVersion = tag?.TrimStart('v', 'V'),
                Error = "Could not parse version",
                ErrorCode = ErrorCodes.Parse,
                ReleaseUrl = htmlUrl ?? ReleasesPage,
                IsPortable = IsPortableInstall()
            };
        }

        bool hasUpdate = latest > localSem;
        string rid = GetCurrentRid();
        string? downloadUrl = null;
        string? assetName = null;

        if (hasUpdate && root.TryGetProperty("assets", out var assets)
            && assets.ValueKind == JsonValueKind.Array)
        {
            string ver = latest.ToString();
            string prefer = BuildSetupAssetName(ver, rid);

            foreach (var a in assets.EnumerateArray())
            {
                string? name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                string? url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) continue;

                if (name.Equals(prefer, StringComparison.OrdinalIgnoreCase)
                    || (name.Contains(rid, StringComparison.OrdinalIgnoreCase)
                        && name.Contains("Setup", StringComparison.OrdinalIgnoreCase)))
                {
                    downloadUrl = url;
                    assetName = name;
                    if (name.Equals(prefer, StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
        }

        // If API listed no matching asset, still offer constructed URL
        if (hasUpdate && downloadUrl is null)
        {
            downloadUrl = BuildSetupDownloadUrl(latest.ToString(), rid);
            assetName = BuildSetupAssetName(latest.ToString(), rid);
        }

        bool portable = IsPortableInstall();
        return new UpdateCheckResult
        {
            LocalVersion = localSem.ToString(),
            LatestVersion = latest.ToString(),
            HasUpdate = hasUpdate,
            DownloadUrl = downloadUrl,
            AssetName = assetName,
            ReleaseUrl = htmlUrl ?? ReleasesPage,
            ReleaseNotes = body is { Length: > 2000 } ? body[..2000] + "…" : body,
            CanSilentInstall = hasUpdate && downloadUrl is not null && !portable,
            IsPortable = portable
        };
    }

    private async Task<UpdateCheckResult?> TryCheckViaAtomAsync(string local, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ReleasesAtom);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        string xml = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!TryParseLatestTagFromAtom(xml, out string tag))
            return null;

        string tagNorm = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag : "v" + tag;
        return BuildFromTag(local, tag, $"https://github.com/{Owner}/{Repo}/releases/tag/{tagNorm}");
    }

    private async Task<UpdateCheckResult?> TryCheckViaLatestRedirectAsync(string local, CancellationToken ct)
    {
        // Do not follow redirects — we need the Location header with the tag
        using var handler = CreateHandler(allowAutoRedirect: false);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AntiLagNext-Updater/1.2.1");

        using var req = new HttpRequestMessage(HttpMethod.Head, ReleasesLatestRedirect);
        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);

        string? location = resp.Headers.Location?.ToString();
        if (string.IsNullOrEmpty(location)
            && resp.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently
                or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
        {
            if (resp.Headers.TryGetValues("Location", out var vals))
                location = vals.FirstOrDefault();
        }

        // Some stacks follow even with AllowAutoRedirect=false for same-host; also try GET body URL
        if (string.IsNullOrEmpty(location) && resp.RequestMessage?.RequestUri is { } finalUri)
        {
            string u = finalUri.ToString();
            if (u.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase))
                location = u;
        }

        if (string.IsNullOrEmpty(location))
        {
            // GET fallback (HEAD sometimes blocked)
            using var getReq = new HttpRequestMessage(HttpMethod.Get, ReleasesLatestRedirect);
            using var getResp = await client.SendAsync(
                    getReq, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            location = getResp.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(location) && getResp.RequestMessage?.RequestUri is { } gu
                && gu.ToString().Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase))
                location = gu.ToString();
        }

        if (string.IsNullOrEmpty(location) || !TryExtractTag(location, out string tag))
            return null;

        string tagUrl = location.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? location
            : $"https://github.com/{Owner}/{Repo}/releases/tag/{(tag.StartsWith('v') ? tag : "v" + tag)}";
        return BuildFromTag(local, tag, tagUrl);
    }

    private static UpdateCheckResult BuildFromTag(string local, string tag, string releaseUrl)
    {
        if (!SemVer.TryParse(tag, out var latest) || !SemVer.TryParse(local, out var localSem))
        {
            return new UpdateCheckResult
            {
                LocalVersion = local,
                LatestVersion = tag.TrimStart('v', 'V'),
                Error = "Could not parse version",
                ErrorCode = ErrorCodes.Parse,
                ReleaseUrl = releaseUrl,
                IsPortable = IsPortableInstall()
            };
        }

        bool hasUpdate = latest > localSem;
        string rid = GetCurrentRid();
        string? downloadUrl = hasUpdate ? BuildSetupDownloadUrl(latest.ToString(), rid) : null;
        string? assetName = hasUpdate ? BuildSetupAssetName(latest.ToString(), rid) : null;
        bool portable = IsPortableInstall();

        // Normalize release URL
        string html = releaseUrl;
        if (!html.Contains("/releases/", StringComparison.OrdinalIgnoreCase))
            html = $"https://github.com/{Owner}/{Repo}/releases/tag/v{latest}";

        return new UpdateCheckResult
        {
            LocalVersion = localSem.ToString(),
            LatestVersion = latest.ToString(),
            HasUpdate = hasUpdate,
            DownloadUrl = downloadUrl,
            AssetName = assetName,
            ReleaseUrl = html,
            CanSilentInstall = hasUpdate && downloadUrl is not null && !portable,
            IsPortable = portable
        };
    }

    private static UpdateCheckResult UpToDate(string local, string releaseUrl) => new()
    {
        LocalVersion = local,
        HasUpdate = false,
        ReleaseUrl = releaseUrl,
        IsPortable = IsPortableInstall()
    };

    private static bool IsAllowedDownloadUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        string host = uri.Host;
        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Contains($"/{Owner}/{Repo}/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool HasUninstallEntry()
    {
        string[] roots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };
        foreach (var root in roots)
        {
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root);
                if (k is null) continue;
                foreach (var sub in k.GetSubKeyNames())
                {
                    using var sk = k.OpenSubKey(sub);
                    var name = sk?.GetValue("DisplayName") as string;
                    if (name is not null
                        && name.Contains("AntiLag Next", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { /* ignore */ }
        }
        return false;
    }

    private static string ReadLocalVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                int plus = info.IndexOf('+');
                if (plus >= 0) info = info[..plus];
                if (SemVer.TryParse(info, out var sv)) return sv.ToString();
                return info.Trim();
            }
            var v = asm.GetName().Version;
            if (v != null) return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch { /* ignore */ }
        return "0.0.0";
    }

    private static HttpClient CreateClient()
    {
        var handler = CreateHandler(allowAutoRedirect: true);
        var c = new HttpClient(handler)
        {
            // Overall request budget; per-check uses linked CTS for tighter limits
            Timeout = TimeSpan.FromMinutes(5)
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AntiLagNext-Updater/1.2.1");
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return c;
    }

    private static SocketsHttpHandler CreateHandler(bool allowAutoRedirect)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // Prefer IPv4 when IPv6 routes black-hole (common on some ISPs)
            ConnectCallback = async (context, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct)
                    .ConfigureAwait(false);
                var ordered = addresses
                    .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                    .ToArray();
                if (ordered.Length == 0)
                    throw new SocketException((int)SocketError.HostNotFound);

                Exception? last = null;
                foreach (var addr in ordered)
                {
                    var socket = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };
                    try
                    {
                        using var reg = ct.Register(() =>
                        {
                            try { socket.Dispose(); } catch { /* ignore */ }
                        });
                        await socket.ConnectAsync(new IPEndPoint(addr, context.DnsEndPoint.Port), ct)
                            .ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        try { socket.Dispose(); } catch { /* ignore */ }
                    }
                }

                throw last ?? new SocketException((int)SocketError.ConnectionRefused);
            }
        };
    }
}
