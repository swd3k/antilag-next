using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Enums;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Native;

namespace AntiLagNext.Infrastructure.Services;

/// <summary>
/// Бенчмарк при первом запуске: scheduling latency + timer jitter → рекомендация профиля (RU).
/// </summary>
public sealed class BenchmarkService : IBenchmarkService
{
    private readonly ITimerManager _timer;
    private readonly ICoreParkingManager _parking;

    public BenchmarkService(ITimerManager timer, ICoreParkingManager parking)
    {
        _timer = timer;
        _parking = parking;
    }

    public async Task<OperationResult<BenchmarkResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            const int samples = 80;
            var latencies = new List<double>(samples);

            for (int i = 0; i < samples; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                latencies.Add(MeasureOnceUs());
                await Task.Delay(15, cancellationToken);
            }

            latencies.Sort();
            double median = latencies[latencies.Count / 2];
            double p99 = latencies[(int)(latencies.Count * 0.99) - 1];
            double max = latencies[^1];

            // Подбор таймера
            var tune = await _timer.TuneAsync(0.5, cancellationToken);
            double stableMs = tune.Success && tune.Value != null ? tune.Value.ActualMs : 1.0;
            double jitter = tune.Success && tune.Value != null ? tune.Value.MeasuredJitterUs : 999;
            // Отпускаем — бенчмарк не должен оставлять таймер активным
            _timer.Release();

            var topo = _parking.DetectTopology();
            string topoText = topo.Success && topo.Value != null ? topo.Value.ToString() : "неизвестно";

            ProfileKind recommended;
            string summary;

            if (max > 500 || p99 > 200)
            {
                recommended = ProfileKind.Gaming;
                summary =
                    $"Обнаружена повышенная scheduling-задержка (P99={p99:F0} мкс, max={max:F0} мкс). " +
                    "Рекомендуется **игровой профиль**: таймер 0.5 мс снижает DPC-джиттер и стабилизирует frame pacing; " +
                    "High Performance + отключение парковки ядер уменьшает «просадки» при всплесках нагрузки. " +
                    "⚠️ Повысит энергопотребление и температуру CPU/GPU. " +
                    $"CPU: {topoText}. Стабильный таймер: {stableMs:F3} мс (джиттер {jitter:F1} мкс).";
            }
            else if (median < 50 && max < 150)
            {
                recommended = ProfileKind.Office;
                summary =
                    $"Система уже достаточно отзывчива (медиана={median:F0} мкс, max={max:F0} мкс). " +
                    "Достаточно **офисного профиля**: таймер 1.0 мс, мягкая парковка (E-cores не трогаем). " +
                    "Агрессивные твики дадут мало выгоды, но увеличат расход энергии. " +
                    $"CPU: {topoText}. Стабильный таймер: {stableMs:F3} мс.";
            }
            else
            {
                recommended = ProfileKind.Gaming;
                summary =
                    $"Средняя latency (медиана={median:F0} мкс, P99={p99:F0} мкс). " +
                    "Для игр рекомендуем **игровой профиль** с таймером ~0.5–1.0 мс и High Performance. " +
                    "HAGS и Game Mode полезны для DX12/Vulkan, но HAGS может потребовать перезагрузки. " +
                    "Очистка working set помогает при нехватке RAM, но не влияет на input lag напрямую. " +
                    $"CPU: {topoText}. Стабильный таймер: {stableMs:F3} мс (джиттер {jitter:F1} мкс).";
            }

            var result = new BenchmarkResult
            {
                MaxSchedulingLatencyUs = max,
                P99SchedulingLatencyUs = p99,
                MedianSchedulingLatencyUs = median,
                TimerJitterUs = jitter,
                StableTimerResolutionMs = stableMs,
                RecommendedProfile = recommended,
                Summary = summary
            };

            return OperationResult<BenchmarkResult>.Ok(result, "Бенчмарк завершён.");
        }
        catch (OperationCanceledException)
        {
            return OperationResult<BenchmarkResult>.Fail("Бенчмарк отменён.");
        }
        catch (Exception ex)
        {
            return OperationResult<BenchmarkResult>.Fail("Ошибка бенчмарка.", detail: ex.Message, ex: ex);
        }
    }

    private static double MeasureOnceUs()
    {
        if (!Kernel32.QueryPerformanceFrequency(out long freq) || freq <= 0) return 0;
        IntPtr h = Kernel32.CreateWaitableTimer(IntPtr.Zero, true, null);
        if (h == IntPtr.Zero) return 0;
        try
        {
            long due = -10_000; // 1 ms
            Kernel32.SetWaitableTimer(h, ref due, 0, null, IntPtr.Zero, false);
            Kernel32.QueryPerformanceCounter(out long t0);
            Kernel32.WaitForSingleObject(h, 50);
            Kernel32.QueryPerformanceCounter(out long t1);
            double elapsedUs = (t1 - t0) * 1_000_000.0 / freq;
            return Math.Max(0, elapsedUs - 1000.0);
        }
        finally
        {
            Kernel32.CloseHandle(h);
        }
    }
}
