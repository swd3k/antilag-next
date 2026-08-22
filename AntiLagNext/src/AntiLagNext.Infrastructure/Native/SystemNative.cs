namespace AntiLagNext.Infrastructure.Native;

/// <summary>
/// Resolve well-known Windows binaries under System32 so elevated runs
/// cannot pick up a PATH-hijacked <c>powercfg.exe</c> / <c>schtasks.exe</c>.
/// Always returns a System32-qualified path (fail closed) — never a bare name.
/// </summary>
public static class SystemNative
{
    public static string Exe(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.IndexOfAny(['&', '|', '<', '>', '^', '%', ';']) >= 0
            || fileName.Contains('\\')
            || fileName.Contains('/'))
        {
            throw new ArgumentException("Native exe name must be a plain file name.", nameof(fileName));
        }

        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return Path.Combine(system, fileName);
    }
}
