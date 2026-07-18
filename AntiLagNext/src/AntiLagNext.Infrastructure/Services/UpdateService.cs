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
/// Prefers github.com (Atom / latest redirect) because many networks poison or block api.github.com.
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

    // Shared handler without custom ConnectCallback (custom sockets broke TLS on some elevated runs).
    private static readonly SocketsHttpHandler SharedHandler = CreateHandler(allowAutoRedirect: true);
    private static readonly HttpClient Http = CreateClient(SharedHandler);

    // Known-good api.github.com fronts (used only if system DNS returns a black-hole IP).
    private static readonly IPAddress[] ApiGithubFallbackIps =
    {
        IPAddress.Parse("140.82.121.6"),
        IPAddress.Parse("140.82.121.5"),
        IPAddress.Parse("140.82.112.6"),
        IPAddress.Parse("140.82.113.6"),
    };

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
        var failures = new List<string>(3);
        Exception? lastFail = null;

        // 1) Atom on github.com FIRST — works when api.github.com DNS/IP is poisoned
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(12));
            var atom = await TryCheckViaAtomAsync(local, cts.Token).ConfigureAwait(false);
            if (atom is not null)
                return atom;
            failures.Add("atom:empty");
        }
        catch (Exception ex) when (!IsCallerCancel(ex, cancellationToken))
        {
            lastFail = ex;
            failures.Add("atom:" + ex.GetType().Name);
        }

        // 2) /releases/latest redirect → tag
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(12));
            var redir = await TryCheckViaLatestRedirectAsync(local, cts.Token).ConfigureAwait(false);
            if (redir is not null)
                return redir;
            failures.Add("redirect:empty");
        }
        catch (Exception ex) when (!IsCallerCancel(ex, cancellationToken))
        {
            lastFail = ex;
            failures.Add("redirect:" + ex.GetType().Name);
        }

        // 3) REST API (optional enrichment) — short budget + DNS fallback IPs
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var api = await TryCheckViaApiAsync(local, cts.Token).ConfigureAwait(false);
            if (api is not null)
                return api;
            failures.Add("api:empty");
        }
        catch (Exception ex) when (!IsCallerCancel(ex, cancellationToken))
        {
            lastFail = ex;
            failures.Add("api:" + ex.GetType().Name);
        }

        var (code, msg) = ClassifyError(lastFail);
        return new UpdateCheckResult
        {
            LocalVersion = local,
            Error = msg,
            ErrorCode = code,
            ReleaseUrl = ReleasesPage,
            IsPortable = IsPortableInstall(),
            // Debug breadcrumb for logs (not shown as primary UI string)
            ReleaseNotes = failures.Count > 0 ? string.Join(",", failures) : null
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

    public static bool TryParseLatestTagFromAtom(string atomXml, out string tag)
    {
        tag = "";
        if (string.IsNullOrWhiteSpace(atomXml)) return false;

        int entryStart = atomXml.IndexOf("<entry", StringComparison.OrdinalIgnoreCase);
        string slice = entryStart >= 0 ? atomXml[entryStart..] : atomXml;

        var linkMatch = Regex.Match(
            slice,
            @"href\s*=\s*""([^""]*/releases/tag/[^""]+)""",
            RegexOptions.IgnoreCase);
        if (linkMatch.Success && TryExtractTag(linkMatch.Groups[1].Value, out tag))
            return true;

        var idMatch = Regex.Match(
            slice,
            @"<id>\s*([^<]+)\s*</id>",
            RegexOptions.IgnoreCase);
        if (idMatch.Success && TryExtractTag(idMatch.Groups[1].Value.Trim(), out tag))
            return true;

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

        m = TagInAtomId.Match(text.Trim());
        if (m.Success)
        {
            tag = m.Value.TrimStart('/');
            return true;
        }

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

        Exception root = ex;
        while (root.InnerException is not null
               && (root is HttpRequestException or TaskCanceledException or AggregateException
                   or IOException))
            root = root.InnerException;

        if (ex is TaskCanceledException or OperationCanceledException or TimeoutException
            || root is TimeoutException or TaskCanceledException or OperationCanceledException)
            return (ErrorCodes.Timeout, "Update check timed out. Try again or open Releases.");

        if (root is SocketException
            || root is IOException
            || ex is HttpRequestException
            || root is HttpRequestException
            || root.GetType().Name.Contains("Authentication", StringComparison.Ordinal)
            || root.GetType().Name.Contains("Security", StringComparison.Ordinal))
            return (ErrorCodes.Network, "Could not reach GitHub. Check network or open Releases manually.");

        // Still treat as network for UI — users cannot act on NRE/IO details
        return (ErrorCodes.Network, "Could not reach GitHub. Check network or open Releases manually.");
    }

    private static bool IsCallerCancel(Exception ex, CancellationToken caller)
    {
        if (caller.IsCancellationRequested
            && ex is OperationCanceledException)
            return true;
        return false;
    }

    private async Task<UpdateCheckResult?> TryCheckViaApiAsync(string local, CancellationToken ct)
    {
        // Prefer plain client; if DNS is poisoned, retry with fixed GitHub IPs via Host header
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
                return await ParseApiResponseAsync(local, resp, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ct.IsCancellationRequested)
        {
            if (ct.IsCancellationRequested) throw;
            // fall through to IP override
        }

        return await TryCheckViaApiWithFallbackIpsAsync(local, ct).ConfigureAwait(false);
    }

    private static async Task<UpdateCheckResult?> TryCheckViaApiWithFallbackIpsAsync(
        string local, CancellationToken ct)
    {
        foreach (var ip in ApiGithubFallbackIps)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = true,
                    AutomaticDecompression = DecompressionMethods.All,
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    ConnectCallback = async (ctx, token) =>
                    {
                        // Force connect to known-good IP while keeping SNI/Host as api.github.com
                        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                        {
                            NoDelay = true
                        };
                        try
                        {
                            using var reg = token.Register(() =>
                            {
                                try { socket.Dispose(); } catch { /* ignore */ }
                            });
                            await socket.ConnectAsync(new IPEndPoint(ip, 443), token)
                                .ConfigureAwait(false);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                            try { socket.Dispose(); } catch { /* ignore */ }
                            throw;
                        }
                    }
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AntiLagNext-Updater/1.2.2");
                client.DefaultRequestHeaders.Host = "api.github.com";
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                using var resp = await client.GetAsync(ReleasesApi, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    continue;
                return await ParseApiResponseAsync(local, resp, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || ct.IsCancellationRequested)
            {
                if (ct.IsCancellationRequested) throw;
                // try next IP
            }
        }

        return null;
    }

    private static async Task<UpdateCheckResult?> ParseApiResponseAsync(
        string local, HttpResponseMessage resp, CancellationToken ct)
    {
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
            string prefer = BuildSetupAssetName(latest.ToString(), rid);
            foreach (var a in assets.EnumerateArray())
            {
                string? name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                string? url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) continue;

                if (name.Equals(prefer, StringComparison.OrdinalIgnoreCase)
                    || name.Contains(rid, StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = url;
                    assetName = name;
                    if (name.Equals(prefer, StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
        }

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
        req.Headers.Accept.ParseAdd("application/atom+xml");
        req.Headers.Accept.ParseAdd("application/xml");
        req.Headers.Accept.ParseAdd("text/xml");
        req.Headers.Accept.ParseAdd("*/*");
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        string xml = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        // GitHub sometimes returns HTML interstitial; reject non-feed
        if (xml.Contains("<html", StringComparison.OrdinalIgnoreCase)
            && !xml.Contains("<feed", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!TryParseLatestTagFromAtom(xml, out string tag))
            return null;

        string tagNorm = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag : "v" + tag;
        return BuildFromTag(local, tag, $"https://github.com/{Owner}/{Repo}/releases/tag/{tagNorm}");
    }

    private async Task<UpdateCheckResult?> TryCheckViaLatestRedirectAsync(string local, CancellationToken ct)
    {
        using var handler = CreateHandler(allowAutoRedirect: false);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AntiLagNext-Updater/1.2.2");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");

        string? location = null;

        // GET is more reliable than HEAD behind some proxies
        using (var getReq = new HttpRequestMessage(HttpMethod.Get, ReleasesLatestRedirect))
        using (var getResp = await client.SendAsync(
                   getReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            location = getResp.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(location)
                && getResp.Headers.TryGetValues("Location", out var vals))
                location = vals.FirstOrDefault();

            if (string.IsNullOrEmpty(location) && getResp.RequestMessage?.RequestUri is { } gu
                && gu.AbsoluteUri.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase))
                location = gu.AbsoluteUri;

            // 200 with final URL after manual non-redirect: parse HTML link as last resort
            if (string.IsNullOrEmpty(location) && getResp.IsSuccessStatusCode)
            {
                string html = await getResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var m = Regex.Match(html, @"/releases/tag/(v?\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
                if (m.Success)
                    location = $"https://github.com/{Owner}/{Repo}/releases/tag/{m.Groups[1].Value}";
            }
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
        LatestVersion = local,
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

    private static HttpClient CreateClient(SocketsHttpHandler handler)
    {
        var c = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        // Minimal defaults — per-request Accept avoids fighting Atom vs JSON
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AntiLagNext-Updater/1.2.2");
        c.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return c;
    }

    private static SocketsHttpHandler CreateHandler(bool allowAutoRedirect)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            // Happy Eyeballs-ish: let OS dual-stack work; do not inject custom sockets for github.com
            ConnectCallback = null
        };
    }
}
