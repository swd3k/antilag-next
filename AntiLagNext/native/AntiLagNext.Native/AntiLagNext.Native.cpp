/*
 * AntiLagNext.Native.dll
 * Низкоуровневые операции: таймер (NtSetTimerResolution) и заглушка GPU Low Latency.
 * Полный NVAPI/ADLX требует проприетарный SDK — экспорты готовы к подключению.
 *
 * Сборка (x64, Release):
 *   cl /LD /O2 /EHsc /DUNICODE /D_UNICODE AntiLagNext.Native.cpp /Fe:AntiLagNext.Native.dll /link ntdll.lib
 * или через CMake (см. CMakeLists.txt).
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>

// --- ntdll timer ---
typedef LONG NTSTATUS;
typedef NTSTATUS (NTAPI *PFN_NtQueryTimerResolution)(PULONG Min, PULONG Max, PULONG Cur);
typedef NTSTATUS (NTAPI *PFN_NtSetTimerResolution)(ULONG Desired, BOOLEAN Set, PULONG Actual);

static HMODULE g_ntdll = nullptr;
static PFN_NtQueryTimerResolution g_NtQuery = nullptr;
static PFN_NtSetTimerResolution g_NtSet = nullptr;

static bool EnsureNtdll()
{
    if (g_ntdll) return g_NtSet != nullptr;
    g_ntdll = GetModuleHandleW(L"ntdll.dll");
    if (!g_ntdll) g_ntdll = LoadLibraryW(L"ntdll.dll");
    if (!g_ntdll) return false;
    g_NtQuery = (PFN_NtQueryTimerResolution)GetProcAddress(g_ntdll, "NtQueryTimerResolution");
    g_NtSet = (PFN_NtSetTimerResolution)GetProcAddress(g_ntdll, "NtSetTimerResolution");
    return g_NtSet != nullptr;
}

extern "C" {

__declspec(dllexport) int Aln_IsAvailable()
{
    // 1 = таймер доступен; NVAPI не включён в open-source сборку
    return EnsureNtdll() ? 1 : 0;
}

/// <summary>
/// Установить разрешение таймера (desired100Ns в единицах 100 нс).
/// setResolution: 1 = set, 0 = release.
/// Возвращает фактический период через outActual; 0 = успех.
/// </summary>
__declspec(dllexport) int Aln_SetTimerResolution(unsigned long desired100Ns, int setResolution, unsigned long* outActual)
{
    if (!EnsureNtdll()) return -1;
    ULONG actual = 0;
    NTSTATUS st = g_NtSet(desired100Ns, setResolution ? TRUE : FALSE, &actual);
    if (outActual) *outActual = actual;
    return (int)st;
}

__declspec(dllexport) int Aln_QueryTimerResolution(unsigned long* minR, unsigned long* maxR, unsigned long* curR)
{
    if (!EnsureNtdll() || !g_NtQuery) return -1;
    ULONG a = 0, b = 0, c = 0;
    NTSTATUS st = g_NtQuery(&a, &b, &c);
    if (minR) *minR = a;
    if (maxR) *maxR = b;
    if (curR) *curR = c;
    return (int)st;
}

/// <summary>
/// Заглушка GPU Low Latency. При линковке NVAPI — заменить на NvAPI_D3D_SetLatencyMarker / Reflex.
/// Сейчас возвращает 1 (не реализовано), чтобы managed-код перешёл на registry-fallback.
/// </summary>
__declspec(dllexport) int Aln_SetGpuLowLatency(int enabled)
{
    (void)enabled;
    // TODO: #ifdef HAS_NVAPI ... NvAPI_Initialize + set low latency
    return 1; // not available → managed registry path
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID reserved)
{
    (void)hModule; (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
        EnsureNtdll();
    }
    return TRUE;
}

} // extern "C"
