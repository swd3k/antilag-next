using System.Security.Principal;
using System.Text.Json;
using AntiLagNext.Infrastructure.Host;

// AntiLag Next CLI
//   AntiLagNext.Cli --apply gaming --silent
//   AntiLagNext.Cli --revert --silent
//   AntiLagNext.Cli --status
// Manifest is asInvoker so --help/--status work without UAC.
// apply/revert require elevation (checked at runtime).

static bool IsElevated()
{
    try
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
    catch
    {
        return false;
    }
}

static int PrintHelp()
{
    Console.WriteLine("""
        AntiLag Next CLI — silent system optimizations

        Usage:
          AntiLagNext.Cli --apply <profile> [--silent]   (requires Administrator)
          AntiLagNext.Cli --revert [--silent]            (requires Administrator)
          AntiLagNext.Cli --status
          AntiLagNext.Cli --help

        Profiles:
          gaming | office | max | default

        Exit codes: 0 = success, 1 = failure
        """);
    return 0;
}

var argsList = args.ToList();
bool silent = argsList.RemoveAll(a =>
    a.Equals("--silent", StringComparison.OrdinalIgnoreCase)
    || a.Equals("-s", StringComparison.OrdinalIgnoreCase)
    || a.Equals("/silent", StringComparison.OrdinalIgnoreCase)) > 0;

if (argsList.Count == 0
    || argsList.Exists(a => a is "-h" or "--help" or "/?" or "/help"))
{
    return PrintHelp();
}

string? command = null;
string? profileArg = null;

for (int i = 0; i < argsList.Count; i++)
{
    string a = argsList[i];
    if (a.Equals("--apply", StringComparison.OrdinalIgnoreCase)
        || a.Equals("-a", StringComparison.OrdinalIgnoreCase)
        || a.Equals("/apply", StringComparison.OrdinalIgnoreCase))
    {
        command = "apply";
        if (i + 1 < argsList.Count && !argsList[i + 1].StartsWith('-'))
            profileArg = argsList[++i];
        else
            profileArg = "gaming";
    }
    else if (a.Equals("--revert", StringComparison.OrdinalIgnoreCase)
             || a.Equals("--reset", StringComparison.OrdinalIgnoreCase)
             || a.Equals("-r", StringComparison.OrdinalIgnoreCase))
    {
        command = "revert";
    }
    else if (a.Equals("--status", StringComparison.OrdinalIgnoreCase))
    {
        command = "status";
    }
    else if (command == null && !a.StartsWith('-'))
    {
        command = a.ToLowerInvariant();
        if (command == "apply" && i + 1 < argsList.Count)
            profileArg = argsList[++i];
    }
}

if (command == null)
{
    if (!silent) Console.Error.WriteLine("Unknown arguments. Use --help.");
    return 1;
}

// Mutating commands need admin; fail fast with clear message (no UAC surprise mid-run)
if (command is "apply" or "revert" or "reset")
{
    if (!IsElevated())
    {
        // Always surface elevation errors (even with --silent) — precondition, not op noise
        Console.Error.WriteLine(
            "Administrator rights required for --apply / --revert. " +
            "Re-run from an elevated prompt (or use the AntiLag Next UI).");
        return 1;
    }
}

EngineBootstrap? engine = null;
try
{
    engine = await EngineBootstrap.CreateAsync().ConfigureAwait(false);

    switch (command)
    {
        case "apply":
        {
            var result = await engine.ApplyAsync(profileArg).ConfigureAwait(false);
            if (!silent)
            {
                Console.WriteLine(result.Success ? result.Message : "ERROR: " + result.Message);
                if (!string.IsNullOrWhiteSpace(result.Detail))
                    Console.WriteLine(result.Detail);
            }
            return result.Success ? 0 : 1;
        }
        case "revert":
        case "reset":
        {
            var result = await engine.RevertAsync().ConfigureAwait(false);
            if (!silent)
            {
                Console.WriteLine(result.Success ? result.Message : "ERROR: " + result.Message);
                if (!string.IsNullOrWhiteSpace(result.Detail))
                    Console.WriteLine(result.Detail);
            }
            return result.Success ? 0 : 1;
        }
        case "status":
        {
            var snap = engine.BuildStatusSnapshot();
            Console.WriteLine(JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        default:
            if (!silent) Console.Error.WriteLine($"Unknown command: {command}");
            return 1;
    }
}
catch (Exception ex)
{
    if (!silent)
    {
        Console.Error.WriteLine("Fatal: " + ex.Message);
        if (ex.InnerException != null)
            Console.Error.WriteLine(ex.InnerException.Message);
    }
    return 1;
}
finally
{
    engine?.Dispose();
}
