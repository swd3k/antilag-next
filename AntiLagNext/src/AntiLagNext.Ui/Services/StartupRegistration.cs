using System.Diagnostics;

namespace AntiLagNext.Ui.Services;

/// <summary>
/// Autostart with Windows via Task Scheduler (ONLOGON + HIGHEST) so an elevated
/// app does not prompt UAC every boot after the task is created once as admin.
/// </summary>
internal static class StartupRegistration
{
    public const string TaskName = "AntiLagNext";

    public static string ExePath =>
        Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "AntiLagNext.exe");

    public static bool IsEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{TaskName}\" /FO LIST",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, out string message)
    {
        try
        {
            if (!enabled)
            {
                int code = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
                // 0 = deleted, 1 = not found — both OK for "off"
                message = code is 0 or 1 ? "ok" : "delete failed: " + code;
                return code is 0 or 1;
            }

            string exe = ExePath;
            if (!File.Exists(exe))
            {
                message = "exe missing: " + exe;
                return false;
            }

            // Reject path metacharacters that would break /TR quoting or inject schtasks args
            if (exe.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
            {
                message = "exe path has unsafe characters";
                return false;
            }

            // /RL HIGHEST — elevated without UAC after task is created once as admin
            // --autostart → tray + AutoApplyOnStartup
            // Quote exe path for spaces; leave args outside the inner quotes.
            string tr = $"\\\"{exe}\\\" --autostart";
            string args =
                $"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RL HIGHEST /TR \"{tr}\"";
            int create = RunSchtasks(args);
            if (create != 0)
            {
                message = "create failed: " + create;
                return false;
            }

            message = "ok";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private static int RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null) return -1;
        p.WaitForExit(15000);
        return p.ExitCode;
    }
}
