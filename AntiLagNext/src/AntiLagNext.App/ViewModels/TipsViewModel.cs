using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AntiLagNext.App.ViewModels;

/// <summary>Справочник подсказок: что повышает отзывчивость и какие есть побочные эффекты.</summary>
public partial class TipsViewModel : ViewModelBase
{
    public ObservableCollection<TipItem> Tips { get; } = new()
    {
        new("Разрешение таймера (0.5–1.0 мс)",
            "Снижает джиттер Sleep/таймеров и стабилизирует frame pacing. Windows 11 22H2+ — per-process.",
            "Выше энергопотребление CPU (больше прерываний таймера)."),

        new("High / Ultimate Performance",
            "Отключает агрессивные C-states и throttling, CPU быстрее отвечает на нагрузку.",
            "Сильнее греется и шумит, быстрее разряжается батарея."),

        new("Отключение Core Parking",
            "Ядра не «засыпают» — меньше latency при внезапной нагрузке (игры, звук).",
            "На ноутбуках заметно выше температура. На hybrid CPU можно оставить E-cores."),

        new("Game Mode + отключение Game DVR",
            "Game Mode отдаёт ресурсы foreground-игре; DVR часто даёт micro-stutter.",
            "Потеря записи клипов Xbox Game Bar."),

        new("HAGS (Hardware-accelerated GPU Scheduling)",
            "GPU сам планирует работу — меньше latency на DX12/Vulkan (зависит от драйвера).",
            "Может ухудшить стабильность на старых драйверах; нужна перезагрузка."),

        new("GPU Low Latency / Reflex / Anti-Lag",
            "Сокращает очередь кадров между CPU и GPU (input → display).",
            "Лучше включать в игре; registry — best-effort без NVAPI SDK."),

        new("Empty Working Set",
            "Освобождает RAM у фоновых процессов — полезно при нехватке памяти.",
            "Не снижает input lag напрямую; может вызвать краткие паузы при «разворачивании»."),

        new("Точка восстановления + JSON-бэкап",
            "Перед изменениями AntiLag Next сохраняет значения и (по возможности) restore point.",
            "Квота Windows: часто не чаще 1 точки / 24 ч — бэкап JSON всё равно создаётся."),
    };
}

public sealed class TipItem
{
    public string Title { get; }
    public string Benefit { get; }
    public string Tradeoff { get; }

    public TipItem(string title, string benefit, string tradeoff)
    {
        Title = title;
        Benefit = benefit;
        Tradeoff = tradeoff;
    }
}
