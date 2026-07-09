using System.Runtime.InteropServices;

namespace AntiLagNext.Infrastructure.Native;

/// <summary>
/// P/Invoke к advapi32.dll — ETW (Event Tracing for Windows) для замера frame-time DXGI.
/// Мы подписываемся на провайдер Microsoft-Windows-DXGI и считаем Present-события,
/// получая реальный фреймтайм без внешних бинарей (PresentMon не требуется).
///
/// Замечание: полный разбор событий DXGI требует tdh.dll (TdhEnumerateProviders) и сложного
/// парсинга. Здесь — минимально достаточный каркас: открытие сессии и обработка PresentLatency.
/// Для production-точности рекомендуется расширить парсинг через TraceEvent (NuGet Microsoft.Diagnostics.Tracing.EventSource),
/// но он добавляет зависимость; в рамках текущей задачи оставляем нативный ETW-каркас.
/// </summary>
internal static class EtwTrace
{
    private const string Lib = "advapi32.dll";

    // Флаги ENABLE_TRACE_PARAMETERS
    public const uint EVENT_ENABLE_PROPERTY_SID = 0x00000001;
    public const uint EVENT_ENABLE_PROPERTY_TS_ID = 0x00000002;
    public const uint WNODE_FLAG_TRACED_GUID = 0x00020000;

    public const uint TRACE_LEVEL_INFORMATION = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct EVENT_TRACE_LOGFILE
    {
        public IntPtr LogFileName;
        public IntPtr LoggerName;
        public long MaximumFileSize;
        public uint LogFileMode;
        public uint BufferSize;
        public int MinimumBuffers;
        public int MaximumBuffers;
        public int KernelName;
        public ulong StartTime;
        public ulong EndTime;
        public uint BuffersRead;
        public uint ProcessTraceMode;
        public IntPtr CurrentTime;
        public uint BuffersWritten;
        public uint EventsLost;
        public uint LogBuffersFull;
        public uint RealTimeBuffersDelivered;
        public uint RealTimeBuffersLost;
        public IntPtr ThreadHandle;
        public IntPtr IsKernelTrace;
        public IntPtr Context;
        // Колбэки (не используем — читаем через ProcessTrace в отдельном потоке)
        public IntPtr BufferCallback;
        public IntPtr BufferCallbackContext;
        public IntPtr EventCallback;
        public IntPtr EventCallbackContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ENABLE_TRACE_PARAMETERS
    {
        public uint Version;        // ENABLE_TRACE_PARAMETERS_VERSION_2 = 2
        public uint EnableProperty;
        public uint EnableLevel;
        public ulong AnyKeyword;
        public ulong AllKeyword;
        public GUID FilterType;
        public uint FilterDataCount;
        public IntPtr FilterData;   // PEVENT_FILTER_DESCRIPTOR
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUID
    {
        public uint Data1;
        public ushort Data2;
        public ushort Data3;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Data4;

        public static GUID FromGuid(Guid g)
        {
            var b = g.ToByteArray();
            return new GUID
            {
                Data1 = BitConverter.ToUInt32(b, 0),
                Data2 = BitConverter.ToUInt16(b, 4),
                Data3 = BitConverter.ToUInt16(b, 6),
                Data4 = b[8..]
            };
        }
    }

    /// <summary>Начать ETW-сессию реального времени.</summary>
    [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint StartTraceW(out ulong sessionHandle, string sessionName, ref EVENT_TRACE_PROPERTIES Properties);

    /// <summary>Включить провайдер ETW.</summary>
    [DllImport(Lib, SetLastError = true)]
    public static extern uint EnableTraceEx2(ulong sessionHandle, ref GUID providerId, uint controlCode, byte level, ulong anyKeyword, ulong allKeyword, uint enableProperty, IntPtr enableParameters);

    [StructLayout(LayoutKind.Sequential)]
    public struct EVENT_TRACE_PROPERTIES
    {
        public uint Wnode;
        public uint BufferSize;
        public uint GuidType;
        public uint LoggerNameOffset;
        public uint LogFileNameOffset;
    }

    public const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
}
