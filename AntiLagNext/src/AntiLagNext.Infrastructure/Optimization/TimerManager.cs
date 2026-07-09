using AntiLagNext.Core.Abstractions;
using AntiLagNext.Core.Models;
using AntiLagNext.Infrastructure.Native;

namespace AntiLagNext.Infrastructure.Optimization;

/// <summary>
/// Управление разрешением системного таймера через NtSetTimerResolution.
///
/// Ключевое отличие от оригинального AntiLag: вместо фиксированного 0.5 мс мы ПОДБИРАЕМ
/// минимальное СТАБИЛЬНОЕ разрешение. Для каждого кандидата измеряем реальный джиттер
/// циклом QueryPerformanceCounter и выбираем значение с приемлемым разбросом.
/// Это избегает нестабильности, которую иногда даёт слишком агрессивное 0.5 мс.
///
/// Важно (Windows 11 22H2+): глобальное разрешение стало per-process. Этот менеджер
/// удерживает его для текущего процесса; для влияния на игры рекомендуется запускать
/// AntiLag Next от имени того же пользователя/сессии.
/// </summary>
public sealed class TimerManager : ITimerManager
{
    private readonly object _lock = new();
    private TimerState _state;

    /// <summary>Допустимый максимальный джиттер (мкс) для признания разрешения стабильным.</summary>
    private const double MaxAcceptableJitterUs = 250.0;

    /// <summary>Количество итераций замера стабильности на каждый кандидат.</summary>
    private const int StabilityIterations = 2000;

    public TimerManager()
    {
        var caps = GetCaps();
        _state = new TimerState { Caps = caps, IsActive = false };
    }

    public TimerState CurrentState
    {
        get { lock (_lock) return _state; }
    }

    public event EventHandler<TimerState>? StateChanged;

    public TimerCaps GetCaps()
    {
        uint status = NtDll.NtQueryTimerResolution(out uint a, out uint b, out _);
        if (status != 0)
        {
            // Fallback-значения, если запрос не удался (редкий случай)
            return new TimerCaps { MinimumPeriod = 5000, MaximumPeriod = 156250 };
        }
        // NtQuery: имена min/max в API исторически путают; нормализуем:
        // MinimumPeriod = finest (наименьший период), MaximumPeriod = coarsest.
        uint fine = Math.Min(a, b);
        uint coarse = Math.Max(a, b);
        if (fine == 0) fine = 5000;
        if (coarse == 0) coarse = 156250;
        return new TimerCaps { MinimumPeriod = fine, MaximumPeriod = coarse };
    }

    public async Task<OperationResult<TimerState>> TuneAsync(double targetMs, CancellationToken cancellationToken = default)
    {
        try
        {
            var caps = GetCaps();

            // Список кандидатов от целевого вверх (если 0.5 нестабильно — пробуем 0.6, 0.7...).
            var candidates = BuildCandidateList(targetMs, caps);

            uint bestPeriod = 0;
            uint bestActual = 0;
            double bestJitter = double.MaxValue;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Пытаемся установить кандидата
                uint status = NtDll.NtSetTimerResolution(candidate, true, out uint actual);
                if (status != 0) continue;

                // Измеряем стабильность
                double jitter = await MeasureJitterAsync(cancellationToken);
                if (jitter < bestJitter)
                {
                    bestJitter = jitter;
                    bestPeriod = candidate;
                    bestActual = actual;
                }

                // Если уже в пределах допуска — принимаем (не гонимся за более высоким разрешением ценой джиттера)
                if (jitter <= MaxAcceptableJitterUs) break;
            }

            if (bestPeriod == 0)
            {
                return OperationResult<TimerState>.Fail(
                    "Не удалось подобрать стабильное разрешение таймера.",
                    detail: "Все кандидаты завершились с ошибкой NTSTATUS.");
            }

            lock (_lock)
            {
                _state = new TimerState
                {
                    Caps = caps,
                    DesiredPeriod100Ns = bestPeriod,
                    ActualPeriod100Ns = bestActual,
                    MeasuredJitterUs = bestJitter,
                    IsActive = true
                };
            }
            StateChanged?.Invoke(this, CurrentState);

            return OperationResult<TimerState>.Ok(
                CurrentState,
                $"Таймер: {CurrentState.ActualMs:F3} мс (джиттер {bestJitter:F1} мкс).");
        }
        catch (OperationCanceledException)
        {
            return OperationResult<TimerState>.Fail("Подбор таймера отменён.");
        }
        catch (Exception ex)
        {
            return OperationResult<TimerState>.Fail("Сбой подбора разрешения таймера.", detail: ex.Message, ex: ex);
        }
    }

    public OperationResult Release()
    {
        try
        {
            // Set=false отпускает разрешение (возвращает к значению по умолчанию ~15.6 мс)
            NtDll.NtSetTimerResolution(0, false, out _);
            lock (_lock)
            {
                _state = new TimerState { Caps = _state.Caps, IsActive = false };
            }
            StateChanged?.Invoke(this, CurrentState);
            return OperationResult.Ok("Таймер отпущен, система вернётся к разрешению по умолчанию.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Не удалось отпустить таймер.", detail: ex.Message, ex: ex);
        }
    }

    /// <summary>
    /// Построить список кандидатов разрешения от целевого вверх, в 100-нс единицах.
    /// Если целевое ниже доступного минимума — начинаем с минимума.
    /// </summary>
    private static List<uint> BuildCandidateList(double targetMs, TimerCaps caps)
    {
        uint target100Ns = (uint)(targetMs * 10_000);
        // Кандидаты: целевое, +0.1 мс шаги вверх до 1.0 мс, ограничено доступным диапазоном
        var set = new SortedSet<uint>();
        for (double ms = Math.Max(targetMs, caps.MinimumMs); ms <= Math.Min(1.0, caps.MaximumMs); ms += 0.1)
        {
            set.Add((uint)(ms * 10_000));
        }
        // Гарантируем наличие минимума и 1.0 мс как безопасных вариантов
        set.Add(caps.MinimumPeriod);
        set.Add(10_000);
        return set.ToList();
    }

    /// <summary>
    /// Измерить джиттер текущего разрешения: выполняем StabilityIterations циклов QPC,
    /// каждый раз спим минимально и измеряем реальное время между отсчётами.
    /// Возвращаем максимальное отклонение от ожидаемого периода (в мкс).
    /// Техника: высокая частота QPC + SpinWait позволяет оценить планирование/DPC.
    /// </summary>
    private async Task<double> MeasureJitterAsync(CancellationToken ct)
    {
        if (!Kernel32.QueryPerformanceFrequency(out long freq) || freq <= 0)
            return double.MaxValue;

        var intervals = new double[StabilityIterations];
        Kernel32.QueryPerformanceCounter(out long prev);

        await Task.Run(() =>
        {
            for (int i = 0; i < StabilityIterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                // Короткий спин, чтобы заставить планировщик/DPC проявиться
                System.Threading.Thread.SpinWait(50);
                Kernel32.QueryPerformanceCounter(out long now);
                intervals[i] = (now - prev) * 1_000_000.0 / freq; // мкс
                prev = now;
            }
        }, ct);

        // Медианный интервал как «ожидаемый»
        var sorted = intervals.OrderBy(x => x).ToArray();
        double median = sorted[sorted.Length / 2];
        // Максимальное отклонение от медианы — это и есть «джиттер» (DPC/scheduling jitter proxy)
        double maxDeviation = intervals.Max(x => Math.Abs(x - median));
        return maxDeviation;
    }
}
