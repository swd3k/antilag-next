using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using Microsoft.Win32;

namespace AntiLagNext.Infrastructure.Optimization;

/// <summary>
/// GPU Low Latency / pre-rendered frames через реестр драйверов.
///
/// NVIDIA: HKLM\SYSTEM\CurrentControlSet\Services\nvlddmkm\... и Control Panel:
///   "MaximumPreRenderedFrames", "LowLatencyMode" (зависит от версии драйвера).
/// AMD: похожие ключи в amdkmdag / CCC.
///
/// Полноценный NVAPI/ADLX требует проприетарный SDK и не входит в open-source дистрибутив.
/// При наличии native DLL AntiLagNext.Native.dll вызываем экспорт SetGpuLowLatency (best-effort).
/// </summary>
public sealed class GpuManager : IGpuManager
{
    private readonly IBackupService? _backup;
    private Guid? _sessionId;

    public GpuManager(IBackupService backup) => _backup = backup;

    public void BindBackupSession(Guid sessionId) => _sessionId = sessionId;

    public string DetectVendor()
    {
        try
        {
            // Простая эвристика: наличие сервиса драйвера в реестре
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services?.OpenSubKey("nvlddmkm") != null) return "NVIDIA";
            if (services?.OpenSubKey("amdkmdag") != null || services?.OpenSubKey("amdfendr") != null) return "AMD";
            if (services?.OpenSubKey("igfx") != null || services?.OpenSubKey("igfxCUIService") != null) return "Intel";
        }
        catch { /* ignore */ }
        return "Unknown";
    }

    public OperationResult SetLowLatencyMode(bool enabled)
    {
        try
        {
            string vendor = DetectVendor();
            // Попытка native DLL
            try
            {
                if (NativeBridge.TrySetGpuLowLatency(enabled, out string nativeMsg))
                    return OperationResult.Ok($"GPU Low Latency ({vendor}): {nativeMsg}");
            }
            catch { /* DLL optional */ }

            return vendor switch
            {
                "NVIDIA" => SetNvidiaLowLatency(enabled),
                "AMD" => SetAmdAntiLag(enabled),
                "Intel" => OperationResult.Ok("Intel GPU: Low Latency через драйвер Xe не автоматизируется (ручная настройка в Graphics Command Center)."),
                _ => OperationResult.Fail("GPU-вендор не определён. Установите драйвер NVIDIA/AMD.")
            };
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Не удалось применить GPU Low Latency.", detail: ex.Message, ex: ex);
        }
    }

    public OperationResult SetMaxPreRenderedFrames(int frames)
    {
        if (frames <= 0)
            return OperationResult.Ok("Pre-rendered frames: без изменений.");

        frames = Math.Clamp(frames, 1, 8);
        try
        {
            // Классический OpenGL/DX pre-rendered frames (NVIDIA)
            const string path = @"SOFTWARE\NVIDIA Corporation\Global\NVTweak";
            SnapshotAndSetDword(Registry.LocalMachine, path, "MaxFramesAllowed", frames);

            // Также DX9/11 app-specific default
            const string dxPath = @"SOFTWARE\Microsoft\DirectX";
            // Не все системы имеют этот ключ — best-effort
            try
            {
                SnapshotAndSetDword(Registry.LocalMachine, dxPath, "MaxFrameLatency", frames);
            }
            catch { /* optional */ }

            return OperationResult.Ok($"Max pre-rendered frames = {frames}.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Не удалось ограничить очередь кадров.", detail: ex.Message, ex: ex);
        }
    }

    public OperationResult<string> GetStatusSummary()
    {
        string vendor = DetectVendor();
        return OperationResult<string>.Ok($"GPU: {vendor}. Low Latency: registry/native best-effort.");
    }

    private OperationResult SetNvidiaLowLatency(bool enabled)
    {
        // Документированные Control Panel-ключи меняются; пишем известные legacy-ключи.
        // Современный Reflex лучше включать в самой игре / GeForce Experience.
        const string path = @"SOFTWARE\NVIDIA Corporation\Global\NVTweak";
        try
        {
            SnapshotAndSetDword(Registry.LocalMachine, path, "LowLatencyMode", enabled ? 1 : 0);
            return OperationResult.Ok(enabled
                ? "NVIDIA: Low Latency Mode (registry) включён. Для Reflex — в настройках игры."
                : "NVIDIA: Low Latency Mode (registry) выключен.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("NVIDIA registry write failed.", detail: ex.Message, ex: ex);
        }
    }

    private OperationResult SetAmdAntiLag(bool enabled)
    {
        // AMD Anti-Lag historically via CCC / registry under AMD\CN
        const string path = @"SOFTWARE\AMD\CN";
        try
        {
            SnapshotAndSetDword(Registry.LocalMachine, path, "AntiLag", enabled ? 1 : 0);
            return OperationResult.Ok(enabled
                ? "AMD: Anti-Lag (registry) включён. Проверьте Radeon Software."
                : "AMD: Anti-Lag (registry) выключен.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "AMD registry write failed (проверьте Radeon Software вручную).",
                detail: ex.Message, ex: ex);
        }
    }

    private void SnapshotAndSetDword(RegistryKey root, string keyPath, string valueName, int value)
    {
        if (_backup != null && _sessionId is Guid sid)
        {
            using var key = root.OpenSubKey(keyPath, false);
            var existing = key?.GetValue(valueName);
            _backup.SnapshotRegistryValue(sid, new RegistryBackupEntry
            {
                Hive = root.Name.Contains("LOCAL_MACHINE") ? "HKLM" : "HKCU",
                KeyPath = keyPath,
                ValueName = valueName,
                ValueKind = (int)RegistryValueKind.DWord,
                SerializedValue = existing?.ToString(),
                WasMissing = existing == null
            });
        }

        using var write = root.CreateSubKey(keyPath, true)
            ?? throw new InvalidOperationException(keyPath);
        write.SetValue(valueName, value, RegistryValueKind.DWord);
    }
}

/// <summary>
/// Опциональная загрузка AntiLagNext.Native.dll (C++) для NVAPI-подобных вызовов.
/// </summary>
internal static class NativeBridge
{
    [System.Runtime.InteropServices.DllImport("AntiLagNext.Native.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int Aln_SetGpuLowLatency(int enabled);

    [System.Runtime.InteropServices.DllImport("AntiLagNext.Native.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int Aln_IsAvailable();

    public static bool TrySetGpuLowLatency(bool enabled, out string message)
    {
        try
        {
            if (Aln_IsAvailable() == 0)
            {
                message = "Native DLL без NVAPI.";
                return false;
            }
            int rc = Aln_SetGpuLowLatency(enabled ? 1 : 0);
            message = rc == 0 ? "OK (native)" : $"native rc={rc}";
            return rc == 0;
        }
        catch (DllNotFoundException)
        {
            message = "AntiLagNext.Native.dll не найдена.";
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            message = "Экспорт Aln_SetGpuLowLatency отсутствует.";
            return false;
        }
    }
}
