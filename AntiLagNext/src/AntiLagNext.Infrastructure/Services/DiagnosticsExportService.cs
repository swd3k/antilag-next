using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Core.Settings;
using AntiLagNext.Infrastructure.Storage;

namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// Packages local troubleshooting data into a zip under AppPaths.DiagnosticsDirectory.
/// No secrets, no full registry dumps, no entire backup history, no telemetry phone-home.
/// </summary>
public sealed class DiagnosticsExportService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IAuditService? _audit;
    private readonly IDriftService? _drift;

    public DiagnosticsExportService(IAuditService? audit = null, IDriftService? drift = null)
    {
        _audit = audit;
        _drift = drift;
    }

    /// <summary>
    /// Create a diagnostics zip. Returns the full path on success.
    /// </summary>
    /// <param name="logsText">
    /// Optional UI/host log ring buffer. When null, writes a placeholder noting host may inject logs.
    /// </param>
    public OperationResult<string> ExportZip(string? logsText = null)
    {
        try
        {
            AppPaths.EnsureDirectories();
            string dir = AppPaths.DiagnosticsDirectory;
            Directory.CreateDirectory(dir);

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string zipPath = Path.Combine(dir, $"antilag-diagnostics-{stamp}.zip");

            // Avoid clobber if two exports share the same second
            if (File.Exists(zipPath))
            {
                stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
                zipPath = Path.Combine(dir, $"antilag-diagnostics-{stamp}.zip");
            }

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                TryAddText(zip, "meta.json", BuildMetaJson());
                TryAddText(zip, "settings-redacted.json", BuildRedactedSettingsJson());
                TryCopyFile(zip, "active-state.json", AppPaths.ActiveStateFile);
                TryCopyFile(zip, "desired_state.json", AppPaths.DesiredStateFile);
                TryCopyFile(zip, "incomplete-apply.json", AppPaths.IncompleteApplyFile);
                TryAddText(zip, "logs-snapshot.txt", BuildLogsSnapshot(logsText));
                TryAddAudit(zip);
                TryAddDrift(zip);
                TryAddBackupIndex(zip);
            }

            if (!File.Exists(zipPath))
                return OperationResult<string>.Fail("Diagnostics export failed: zip was not created.");

            PruneOldDiagnostics(dir, keep: 15);
            return OperationResult<string>.Ok(zipPath, "Diagnostics zip created.");
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Fail(
                "Diagnostics export failed.",
                detail: ex.Message,
                ex: ex);
        }
    }

    /// <summary>Keep newest N diagnostics zips; delete older ones (disk hygiene).</summary>
    private static void PruneOldDiagnostics(string dir, int keep)
    {
        try
        {
            if (keep < 1) keep = 1;
            var files = Directory.GetFiles(dir, "antilag-diagnostics-*.zip")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(keep)
                .ToList();
            foreach (var f in files)
            {
                try { f.Delete(); } catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }
    }

    private static string BuildMetaJson()
    {
        string portableMarker = Path.Combine(AppContext.BaseDirectory, "AntiLagNext.portable");
        var meta = new
        {
            appVersion = ReadAppVersion(),
            exportedUtc = DateTime.UtcNow.ToString("o"),
            osDescription = RuntimeInformation.OSDescription,
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            frameworkDescription = RuntimeInformation.FrameworkDescription,
            isElevated = IsProcessElevated(),
            isPortable = File.Exists(portableMarker),
            baseDirectory = AppContext.BaseDirectory,
            appDataRoot = AppPaths.AppDataRoot
        };
        return JsonSerializer.Serialize(meta, JsonOpts);
    }

    private static string BuildRedactedSettingsJson()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return JsonSerializer.Serialize(new
                {
                    note = "user-settings.json not found"
                }, JsonOpts);
            }

            var settings = JsonStorage.Load<AppSettings>(AppPaths.SettingsFile);
            if (settings == null)
            {
                return JsonSerializer.Serialize(new
                {
                    note = "user-settings.json unreadable"
                }, JsonOpts);
            }

            // Redacted view: flags + profile names/kinds + PluginEnabled only.
            // No full profile payloads (game lists can be large); no future secrets fields.
            var redacted = new
            {
                settings.SchemaVersion,
                settings.UiCulture,
                Theme = settings.Theme.ToString(),
                settings.FirstRunCompleted,
                settings.MonitoringEnabled,
                settings.MonitoringIntervalMs,
                settings.GameAutoSwitchEnabled,
                settings.CreateRestorePoint,
                settings.MaxBackupsToKeep,
                settings.MinimizeToTray,
                settings.StartWithWindows,
                settings.AutoApplyOnStartup,
                settings.UserEnabledOptimization,
                settings.ReleaseTimerOnExit,
                settings.AllowExternalPlugins,
                settings.CheckUpdatesOnStartup,
                settings.LastUpdateCheckUtc,
                settings.ActiveProfileId,
                PluginEnabled = settings.PluginEnabled,
                Profiles = settings.Profiles
                    .Select(p => new
                    {
                        p.Id,
                        p.Name,
                        Kind = p.Kind.ToString()
                    })
                    .ToList()
            };

            return JsonSerializer.Serialize(redacted, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                note = "settings redaction failed",
                error = ex.Message
            }, JsonOpts);
        }
    }

    private static string BuildLogsSnapshot(string? logsText)
    {
        if (!string.IsNullOrWhiteSpace(logsText))
            return logsText;

        return
            "Logs not provided to ExportZip." + Environment.NewLine +
            "Host may inject UI ring-buffer logs via ExportZip(logsText)." + Environment.NewLine +
            "No Serilog file contents are bundled automatically (size / privacy).";
    }

    private void TryAddAudit(ZipArchive zip)
    {
        if (_audit is null)
            return;

        try
        {
            var findings = _audit.Scan();
            TryAddText(zip, "audit-findings.json", JsonSerializer.Serialize(findings, JsonOpts));
        }
        catch (Exception ex)
        {
            TryAddText(zip, "audit-findings.json", JsonSerializer.Serialize(new
            {
                note = "audit scan failed",
                error = ex.Message
            }, JsonOpts));
        }
    }

    private void TryAddDrift(ZipArchive zip)
    {
        if (_drift is null)
            return;

        try
        {
            var entries = _drift.Scan();
            TryAddText(zip, "drift-entries.json", JsonSerializer.Serialize(entries, JsonOpts));
        }
        catch (Exception ex)
        {
            TryAddText(zip, "drift-entries.json", JsonSerializer.Serialize(new
            {
                note = "drift scan failed",
                error = ex.Message
            }, JsonOpts));
        }
    }

    /// <summary>
    /// Optional: list latest backup file names only (no full history payloads).
    /// </summary>
    private static void TryAddBackupIndex(ZipArchive zip)
    {
        try
        {
            if (!Directory.Exists(AppPaths.BackupDirectory))
                return;

            var latest = Directory.EnumerateFiles(AppPaths.BackupDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(5)
                .Select(f => new
                {
                    name = f.Name,
                    sizeBytes = f.Length,
                    lastWriteUtc = f.LastWriteTimeUtc.ToString("o")
                })
                .ToList();

            if (latest.Count == 0)
                return;

            TryAddText(zip, "backup-index.json", JsonSerializer.Serialize(new
            {
                note = "File names only — backup payloads are not included.",
                latest
            }, JsonOpts));
        }
        catch
        {
            // best-effort
        }
    }

    private static void TryCopyFile(ZipArchive zip, string entryName, string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return;

            // Cap individual state files (~2 MB) to keep zip small
            var info = new FileInfo(sourcePath);
            if (info.Length > 2 * 1024 * 1024)
            {
                TryAddText(zip, entryName,
                    JsonSerializer.Serialize(new
                    {
                        note = "source file skipped (too large)",
                        path = sourcePath,
                        sizeBytes = info.Length
                    }, JsonOpts));
                return;
            }

            zip.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
        }
        catch
        {
            // best-effort: skip missing/locked files
        }
    }

    private static void TryAddText(ZipArchive zip, string entryName, string content)
    {
        try
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
        }
        catch
        {
            // best-effort
        }
    }

    private static string ReadAppVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                int plus = info.IndexOf('+');
                if (plus >= 0) info = info[..plus];
                return info.Trim();
            }

            var v = asm.GetName().Version;
            if (v != null) return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            // ignore
        }

        return "0.0.0";
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
