using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;

namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// GitHub Releases updater: check latest SemVer, download Setup, silent Inno install.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    public const string Owner = "swd3k";
    public const string Repo = "antilag-next";
    public const string ReleasesApi = "https://api.github.com/repos/swd3k/antilag-next/releases/latest";
    public const string ReleasesPage = "https://github.com/swd3k/antilag-next/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    public string LocalVersion { get; } = ReadLocalVersion();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        string local = LocalVersion;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    LocalVersion = local,
                    Error = $"GitHub HTTP {(int)resp.StatusCode}",
                    ReleaseUrl = ReleasesPage,
                    IsPortable = IsPortableInstall()
                };
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                return new UpdateCheckResult
                {
                    LocalVersion = local,
                    HasUpdate = false,
                    ReleaseUrl = ReleasesPage,
                    IsPortable = IsPortableInstall()
                };
            }

            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
            {
                return new UpdateCheckResult
                {
                    LocalVersion = local,
                    HasUpdate = false,
                    ReleaseUrl = ReleasesPage,
                    IsPortable = IsPortableInstall()
                };
            }

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
                // Prefer: AntiLagNext-Setup-{ver}-win-{rid}.exe
                string ver = latest.ToString();
                string prefer = $"AntiLagNext-Setup-{ver}-{rid}.exe";
                string preferAlt = $"AntiLagNext-Setup-{rid}.exe"; // legacy name

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
                        // Prefer exact rid match
                        if (name.Equals(prefer, StringComparison.OrdinalIgnoreCase)
                            || (name.Contains(rid, StringComparison.OrdinalIgnoreCase)
                                && name.Contains("Setup", StringComparison.OrdinalIgnoreCase)))
                        {
                            downloadUrl = url;
                            assetName = name;
                            if (name.Equals(prefer, StringComparison.OrdinalIgnoreCase)
                                || name.Equals(preferAlt, StringComparison.OrdinalIgnoreCase))
                                break;
                        }
                    }
                }

                // Fallback: any Setup exe for this rid substring
                if (downloadUrl is null)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        string? name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        string? url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        if (name is null || url is null) continue;
                        if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase)
                            && name.Contains(rid, StringComparison.OrdinalIgnoreCase)
                            && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = url;
                            assetName = name;
                            break;
                        }
                    }
                }
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
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                LocalVersion = local,
                Error = ex.Message,
                ReleaseUrl = ReleasesPage,
                IsPortable = IsPortableInstall()
            };
        }
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

        if (!check.DownloadUrl.StartsWith("https://github.com/swd3k/antilag-next/", StringComparison.OrdinalIgnoreCase)
            && !check.DownloadUrl.StartsWith("https://objects.githubusercontent.com/", StringComparison.OrdinalIgnoreCase)
            && !check.DownloadUrl.Contains("github.com/swd3k/antilag-next", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail("Download URL rejected (not official repo).");
        }

        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "AntiLagNext-update");
            Directory.CreateDirectory(dir);
            string fileName = check.AssetName ?? "AntiLagNext-Setup-update.exe";
            // sanitize filename
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

            // Silent Inno: no wizard, no "folder exists", close app
            string args =
                "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS";
            var psi = new ProcessStartInfo
            {
                FileName = dest,
                Arguments = args,
                UseShellExecute = true,
                // Same elevation as current process when already admin
            };
            Process.Start(psi);

            // Give Setup a moment to start, then exit so files unlock
            await Task.Delay(800, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok("Updater started; application will exit.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Update download/install failed.", detail: ex.Message, ex: ex);
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

            // Inno unregister keys (any arch AppId)
            if (HasUninstallEntry())
                return false;

            return true;
        }
        catch
        {
            return true;
        }
    }

    private static bool HasUninstallEntry()
    {
        // Match installer AppIds / DisplayName
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
                // strip +gitsha
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
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AntiLagNext-Updater/1.2.0");
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }
}
