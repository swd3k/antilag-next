using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AntiLagNext.Core.Localization;

namespace AntiLagNext.Infrastructure.Localization;

/// <summary>
/// Language packs: disk i18n/*.json + embedded resources + built-in RU/EN fallbacks.
/// Never leave UI showing raw keys like "page.dashboard.title".
/// </summary>
public sealed class JsonLocalizationService : ILocalizationService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _searchRoots = new();
    private string _culture = "ru";

    public string CurrentCulture => _culture;

    public IReadOnlyList<string> AvailableCultures { get; private set; } = new[] { "ru", "en" };

    public event EventHandler? CultureChanged;

    public JsonLocalizationService(string? searchRoot = null)
    {
        CollectSearchRoots(searchRoot);
        DiscoverCultures();
        Load(_culture);
        Trace.TraceInformation(
            "i18n ready culture={0} keys={1} roots=[{2}]",
            _culture, _map.Count, string.Join(" | ", _searchRoots));
    }

    private void CollectSearchRoots(string? preferred)
    {
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try
            {
                p = Path.GetFullPath(p);
                if (!_searchRoots.Contains(p, StringComparer.OrdinalIgnoreCase))
                    _searchRoots.Add(p);
            }
            catch { /* ignore bad paths */ }
        }

        Add(preferred);
        Add(Path.Combine(AppContext.BaseDirectory, "i18n"));

        try
        {
            var entry = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(entry))
                Add(Path.Combine(Path.GetDirectoryName(entry)!, "i18n"));
        }
        catch { /* ignore */ }

        try
        {
            // %APPDATA%\AntiLagNext\i18n (optional user overrides)
            Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AntiLagNext", "i18n"));
        }
        catch { /* ignore */ }
    }

    public void SetCulture(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return;
        culture = NormalizeCulture(culture);
        if (culture == _culture && _map.Count > 50) return;
        _culture = culture;
        Load(_culture);
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string NormalizeCulture(string culture)
    {
        culture = culture.Trim().ToLowerInvariant().Replace('_', '-');
        // "ru-RU" / "ru-ru" → "ru"
        int dash = culture.IndexOf('-');
        if (dash > 0) culture = culture[..dash];
        return culture;
    }

    public string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (_map.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            return v;
        // Last-chance: never return dotted technical keys if we know a friendly stub
        return BuiltInRuEn.Lookup(key, _culture) ?? key;
    }

    public string Tf(string key, params object[] args)
    {
        var fmt = T(key);
        try { return args.Length == 0 ? fmt : string.Format(fmt, args); }
        catch { return fmt; }
    }

    private void DiscoverCultures()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ru", "en" };
        foreach (var root in _searchRoots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var f in Directory.GetFiles(root, "*.json"))
                {
                    var n = Path.GetFileNameWithoutExtension(f);
                    if (!string.IsNullOrEmpty(n) && n.Length >= 2)
                        found.Add(NormalizeCulture(n));
                }
            }
            catch (Exception ex)
            {
                Trace.TraceInformation("i18n discover {0}: {1}", root, ex.Message);
            }
        }
        AvailableCultures = found.OrderBy(x => x).ToList();
    }

    private void Load(string culture)
    {
        _map.Clear();
        culture = NormalizeCulture(culture);

        // Layered merge: EN base → preferred culture → built-in complete maps
        MergeDisk("en");
        MergeEmbedded("en");
        if (!string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase))
        {
            MergeDisk(culture);
            MergeEmbedded(culture);
        }

        // Always apply complete built-ins for any missing keys (language-aware)
        BuiltInRuEn.MergeInto(_map, culture);

        if (_map.Count < 20)
            Trace.TraceWarning("i18n map suspiciously small ({0} keys)", _map.Count);
    }

    private void MergeDisk(string culture)
    {
        foreach (var root in _searchRoots)
        {
            string path = Path.Combine(root, culture + ".json");
            if (!File.Exists(path)) continue;
            try
            {
                // Explicit UTF-8 (no BOM required)
                var json = File.ReadAllText(path, Encoding.UTF8);
                MergeJson(json, path);
                Trace.TraceInformation("i18n loaded disk: {0}", path);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("i18n disk fail {0}: {1}", path, ex.Message);
            }
        }
    }

    private void MergeEmbedded(string culture)
    {
        // Prefer entry assembly (AntiLagNext), then all loaded
        var assemblies = new List<Assembly>();
        try
        {
            var entry = Assembly.GetEntryAssembly();
            if (entry != null) assemblies.Add(entry);
        }
        catch { /* ignore */ }

        assemblies.Add(typeof(JsonLocalizationService).Assembly);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assemblies.Contains(asm))
                assemblies.Add(asm);
        }

        string needle = culture + ".json";
        foreach (var asm in assemblies)
        {
            string[] names;
            try { names = asm.GetManifestResourceNames(); }
            catch { continue; }

            foreach (var res in names)
            {
                if (!res.EndsWith(needle, StringComparison.OrdinalIgnoreCase)
                    && !res.Contains($".i18n.{culture}.json", StringComparison.OrdinalIgnoreCase)
                    && !res.EndsWith($"i18n.{culture}.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var stream = asm.GetManifestResourceStream(res);
                    if (stream == null) continue;
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var json = reader.ReadToEnd();
                    MergeJson(json, res);
                    Trace.TraceInformation("i18n loaded embedded: {0}", res);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("i18n embedded fail {0}: {1}", res, ex.Message);
                }
            }
        }
    }

    private void MergeJson(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts);
        if (dict == null || dict.Count == 0)
        {
            Trace.TraceWarning("i18n empty deserialize: {0}", source);
            return;
        }
        foreach (var kv in dict)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value is null) continue;
            _map[kv.Key.Trim()] = kv.Value;
        }
    }
}

/// <summary>Complete RU/EN fallback so UI never shows raw keys if JSON missing.</summary>
internal static class BuiltInRuEn
{
    private static readonly Dictionary<string, (string Ru, string En)> Map = Build();

    public static void MergeInto(ConcurrentDictionary<string, string> target, string culture)
    {
        bool ru = !string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase);
        foreach (var kv in Map)
            target.TryAdd(kv.Key, ru ? kv.Value.Ru : kv.Value.En);
    }

    public static string? Lookup(string key, string culture)
    {
        if (!Map.TryGetValue(key, out var pair)) return null;
        return string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase) ? pair.En : pair.Ru;
    }

    private static Dictionary<string, (string Ru, string En)> Build()
    {
        var d = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["app.name"] = ("AntiLag Next", "AntiLag Next"),
            ["nav.dashboard"] = ("Панель", "Dashboard"),
            ["nav.profiles"] = ("Игры и профили", "Games & Profiles"),
            ["nav.monitoring"] = ("Аналитика", "Analytics"),
            ["nav.plugins"] = ("Плагины", "Plugins"),
            ["nav.backups"] = ("Бэкапы", "Backups"),
            ["nav.tips"] = ("Советы", "Tips"),
            ["nav.settings"] = ("Настройки", "Settings"),
            ["nav.github"] = ("GitHub", "GitHub"),
            ["nav.system"] = ("СИСТЕМА", "SYSTEM"),

            ["page.dashboard.title"] = ("Панель управления", "Dashboard"),
            ["page.dashboard.subtitle"] = ("Статус системы и управление оптимизацией", "System status and optimization controls"),
            ["page.profiles.title"] = ("Игры и профили", "Games & Profiles"),
            ["page.profiles.subtitle"] = ("Автопереключение по запущенным играм", "Auto-switch based on running games"),
            ["page.monitoring.title"] = ("Мониторинг и журнал", "Monitoring & Logs"),
            ["page.monitoring.subtitle"] = ("Задержка планировщика Windows", "Windows scheduler latency"),
            ["page.plugins.title"] = ("Плагины", "Plugins"),
            ["page.plugins.subtitle"] = ("Дополнительные модули оптимизации", "Optional optimization modules"),
            ["page.backups.title"] = ("Снимки и бэкапы", "Snapshots & Backups"),
            ["page.backups.subtitle"] = ("Точки восстановления и JSON-снимки", "Restore points and state backups"),
            ["page.tips.title"] = ("Что делают твики", "Tweak explanations"),
            ["page.tips.subtitle"] = ("Польза и компромиссы", "Benefits and tradeoffs"),
            ["page.settings.title"] = ("Настройки", "Settings"),
            ["page.settings.subtitle"] = ("Поведение и оформление", "Behavior and appearance"),

            ["dash.cta.enable"] = ("ВКЛЮЧИТЬ ОПТИМИЗАЦИЮ", "ENABLE OPTIMIZATION"),
            ["dash.cta.recal"] = ("ПЕРЕКАЛИБРОВАТЬ", "RE-CALIBRATE"),
            ["dash.reset"] = ("СБРОСИТЬ ВСЁ", "RESET ALL"),
            ["dash.idle"] = ("БАЗА В ПОКОЕ", "IDLE BASELINE"),
            ["dash.max"] = ("СЕЙЧАС МАКС", "NOW MAX"),
            ["dash.timer"] = ("ТАЙМЕР · ПРОФИЛЬ", "TIMER · PROFILE"),
            ["dash.success"] = ("УСПЕХ", "SUCCESS"),
            ["dash.live"] = ("LIVE", "LIVE"),
            ["dash.ready"] = ("СИСТЕМА ГОТОВА", "SYSTEM READY"),
            ["dash.optimized"] = ("ОПТИМИЗИРОВАНО", "OPTIMIZED"),
            ["dash.disclaimer"] = (
                "Оценка задержки планировщика Windows (не FPS и не ping). Успех = ниже база в покое.",
                "Windows scheduler latency proxy (not FPS / not ping). Success = lower idle baseline."),
            ["dash.chart.title"] = ("История задержки", "Latency history"),
            ["dash.chart.sub"] = ("МЕДИАНА · ПРОКСИ ПЛАНИРОВЩИКА", "MEDIAN · SCHEDULER PROXY"),
            ["dash.chart.hint"] = (
                "График — медиана. MAX на карточке. Клики по UI дают пики (шум).",
                "Chart = median. MAX is on the card. UI clicks cause noise spikes."),
            ["dash.toggles"] = ("ПЕРЕКЛЮЧАТЕЛИ", "TOGGLES"),
            ["dash.toggles.sub"] = ("Применить / перекалибровать", "Apply / re-calibrate"),
            ["dash.bench"] = ("БЕНЧ", "BENCH"),
            ["dash.toggle.timer"] = ("Разрешение таймера", "Timer resolution"),
            ["dash.toggle.power"] = ("Высокая производительность", "High Performance"),
            ["dash.toggle.parking"] = ("Все ядра активны", "All cores active"),
            ["dash.toggle.gamemode"] = ("Game Mode / DVR", "Game Mode / DVR"),
            ["dash.toggle.hags"] = ("HAGS", "HAGS"),
            ["dash.toggle.gpu"] = ("Низкая задержка GPU", "GPU Low Latency"),
            ["dash.toggle.memory"] = ("Очистка памяти", "Memory trim"),
            ["dash.zone.green"] = ("≤50 µs — норма", "≤50 µs good"),
            ["dash.zone.yellow"] = ("≤150 — заметно", "≤150 elevated"),
            ["dash.zone.red"] = (">150 — высоко", ">150 high"),
            ["dash.first.run"] = ("ПЕРВЫЙ ЗАПУСК", "FIRST RUN"),
            ["dash.first.run.hint"] = ("Короткий бенчмарк подберёт профиль.", "A short benchmark will suggest a profile."),
            ["dash.first.run.cta"] = ("ЗАПУСТИТЬ БЕНЧМАРК", "RUN BENCHMARK"),

            ["mon.start"] = ("СТАРТ", "START"),
            ["mon.stop"] = ("СТОП", "STOP"),
            ["mon.clear"] = ("ОЧИСТИТЬ", "CLEAR"),
            ["mon.alerts.on"] = ("АЛЕРТЫ ВКЛ", "ALERTS ON"),
            ["mon.alerts.off"] = ("АЛЕРТЫ ВЫКЛ", "ALERTS OFF"),
            ["mon.running"] = ("МОНИТОРИНГ ИДЁТ", "MONITORING RUNNING"),
            ["mon.dismiss"] = ("ЗАКРЫТЬ", "DISMISS"),
            ["mon.session"] = ("СЕССИЯ", "SESSION"),
            ["mon.chart.title"] = ("История задержки", "Latency history"),
            ["mon.chart.sub"] = ("МЕДИАНА · UI ~8 Гц", "MEDIAN · UI ~8 Hz"),
            ["mon.median"] = ("МЕДИАНА", "MEDIAN"),
            ["mon.max.now"] = ("МАКС СЕЙЧАС", "MAX NOW"),
            ["mon.idle.base"] = ("БАЗА ПОКОЯ", "IDLE BASE"),
            ["mon.peak.p99"] = ("ПИК / P99", "PEAK / P99"),
            ["mon.log.title"] = ("ЖУРНАЛ ВЫСОКОЙ ЗАДЕРЖКИ", "HIGH LATENCY LOG"),

            ["profiles.list.title"] = ("ПРОФИЛИ", "PROFILES"),
            ["profiles.list.sub"] = ("Пресеты и автопереключение", "Presets and auto-switch"),
            ["profiles.save"] = ("СОХРАНИТЬ", "SAVE"),
            ["profiles.new"] = ("НОВЫЙ ПРОФИЛЬ", "NEW PROFILE"),
            ["profiles.delete"] = ("УДАЛИТЬ", "DELETE"),
            ["profiles.game.exes"] = ("EXE ИГР (АВТОПЕРЕКЛЮЧЕНИЕ)", "GAME EXES (AUTO-SWITCH)"),
            ["profiles.add"] = ("ДОБАВИТЬ", "ADD"),
            ["profiles.apply"] = ("ПРИМЕНИТЬ ПРОФИЛЬ", "APPLY PROFILE"),
            ["profiles.saved"] = ("Профили сохранены", "Profiles saved"),
            ["profiles.custom.name"] = ("Пользовательский {0}", "Custom {0}"),
            ["profiles.custom.desc"] = (
                "Свои настройки. Редактируйте переключатели на панели.",
                "Your own settings. Edit toggles on the Dashboard."),
            ["profiles.delete.only.custom"] = (
                "Удалять можно только пользовательские профили.",
                "Only custom profiles can be deleted."),
            ["profiles.delete.confirm"] = ("Удалить профиль «{0}»?", "Delete profile “{0}”?"),
            ["profiles.delete.done"] = ("Профиль «{0}» удалён", "Profile “{0}” deleted"),
            ["profiles.kind.custom"] = ("Пользовательский", "Custom"),
            ["profiles.kind.builtin"] = ("Встроенный", "Built-in"),

            ["plugins.title"] = ("ПЛАГИНЫ", "PLUGINS"),
            ["plugins.subtitle"] = ("Ядро + расширения. DLL — в /plugins", "Core + extensions. DLLs in /plugins"),
            ["plugins.impact"] = ("Влияние на задержку", "Latency impact"),
            ["plugins.builtin"] = ("Встроенный", "Built-in"),
            ["plugins.external"] = ("Внешний", "External"),
            ["plugins.apply"] = ("ПРИМЕНИТЬ ПРОФИЛЬ + ПЛАГИНЫ", "APPLY PROFILE + PLUGINS"),
            ["plugins.save"] = ("СОХРАНИТЬ ФЛАГИ", "SAVE FLAGS"),
            ["plugins.help"] = (
                "Модули ядра — переключатели на панели. Расширения включайте отдельно, сохраните флаги, затем примените.",
                "Core modules follow Dashboard toggles. Enable extensions, save flags, then apply."),

            ["plugin.timer.name"] = ("Разрешение таймера", "Timer resolution"),
            ["plugin.timer.desc"] = ("Удержание NtSetTimerResolution — сильный эффект на джиттер.", "NtSetTimerResolution hold — high impact on jitter."),
            ["plugin.power.name"] = ("Питание и активность ядер", "Power & core activity"),
            ["plugin.power.desc"] = ("High Performance, все ядра активны. Ядро профиля.", "High Performance, all cores active. Core profile."),
            ["plugin.gpu.name"] = ("GPU / HAGS / низкая задержка", "GPU / HAGS / low latency"),
            ["plugin.gpu.desc"] = ("HAGS и registry low-latency. Может понадобиться перезагрузка.", "HAGS + driver low-latency registry. Reboot may be needed."),
            ["plugin.net.name"] = ("Сеть / MMCSS", "Network / MMCSS"),
            ["plugin.net.desc"] = ("Приоритеты MMCSS Games. Включается отдельно.", "MMCSS Games priorities. Opt-in."),
            ["plugin.prio.name"] = ("Приоритет процесса High", "Process priority High"),
            ["plugin.prio.desc"] = ("Класс High для AntiLag. Экспериментально.", "High priority class for AntiLag. Experimental."),

            ["impact.high"] = ("Высокое", "High"),
            ["impact.medium"] = ("Среднее", "Medium"),
            ["impact.low"] = ("Низкое", "Low"),
            ["impact.experimental"] = ("Эксперимент", "Experimental"),
            ["impact.none"] = ("Нет", "None"),

            ["backups.refresh"] = ("ОБНОВИТЬ", "REFRESH"),
            ["backups.restore"] = ("ВОССТАНОВИТЬ", "RESTORE"),
            ["backups.delete"] = ("УДАЛИТЬ", "DELETE"),
            ["backups.open.folder"] = ("ОТКРЫТЬ ПАПКУ", "OPEN FOLDER"),
            ["backups.detail"] = ("ДЕТАЛИ СНИМКА", "SNAPSHOT DETAIL"),

            ["tips.benefit"] = ("Польза", "Benefit"),
            ["tips.tradeoff"] = ("Компромисс", "Tradeoff"),
            ["tip.timer.title"] = ("Разрешение таймера (0.5–1.0 мс)", "Timer resolution (0.5–1.0 ms)"),
            ["tip.timer.benefit"] = ("Снижает джиттер Sleep/таймеров.", "Lowers Sleep/timer jitter."),
            ["tip.timer.tradeoff"] = ("Выше энергопотребление CPU.", "Higher CPU power use."),
            ["tip.power.title"] = ("High / Ultimate Performance", "High / Ultimate Performance"),
            ["tip.power.benefit"] = ("CPU быстрее отвечает на нагрузку.", "CPU reacts faster to load."),
            ["tip.power.tradeoff"] = ("Сильнее греется, шумит, сажает батарею.", "Hotter, louder, drains battery."),
            ["tip.cores.title"] = ("Все ядра активны", "All cores active"),
            ["tip.cores.benefit"] = ("Ядра не уходят в простой — меньше задержки.", "Cores stay awake — lower latency."),
            ["tip.cores.tradeoff"] = ("На ноутбуках выше температура.", "Higher laptop temperatures."),
            ["tip.gamemode.title"] = ("Game Mode + отключение Game DVR", "Game Mode + disable Game DVR"),
            ["tip.gamemode.benefit"] = ("Больше ресурсов foreground-игре.", "More resources for the foreground game."),
            ["tip.gamemode.tradeoff"] = ("Нет клипов Xbox Game Bar.", "No Xbox Game Bar clips."),
            ["tip.hags.title"] = ("HAGS", "HAGS"),
            ["tip.hags.benefit"] = ("GPU сам планирует работу.", "GPU schedules its own work."),
            ["tip.hags.tradeoff"] = ("Нужна перезагрузка; старые драйверы могут глючить.", "Reboot required; old drivers may glitch."),
            ["tip.gpu.title"] = ("GPU Low Latency", "GPU Low Latency"),
            ["tip.gpu.benefit"] = ("Короче очередь кадров CPU→GPU.", "Shorter CPU→GPU frame queue."),
            ["tip.gpu.tradeoff"] = ("Лучше включать в игре.", "Prefer in-game toggles."),
            ["tip.mem.title"] = ("Очистка working set", "Empty working set"),
            ["tip.mem.benefit"] = ("Освобождает RAM у фона.", "Frees RAM from background processes."),
            ["tip.mem.tradeoff"] = ("Не снижает задержку ввода напрямую.", "Does not cut input lag directly."),
            ["tip.backup.title"] = ("Точка восстановления + JSON", "Restore point + JSON"),
            ["tip.backup.benefit"] = ("Снимок перед изменениями.", "Snapshot before changes."),
            ["tip.backup.tradeoff"] = ("Лимит Windows на restore points.", "Windows restore-point quota."),

            ["settings.build"] = ("СБОРКА", "BUILD"),
            ["settings.appearance"] = ("ВНЕШНИЙ ВИД И ЯЗЫК", "APPEARANCE / LANGUAGE"),
            ["settings.theme"] = ("Тема", "Theme"),
            ["settings.language"] = ("Язык интерфейса", "Interface language"),
            ["settings.language.hint"] = ("Файлы: i18n рядом с exe.", "Packs: i18n folder next to EXE."),
            ["settings.safety"] = ("БЕЗОПАСНОСТЬ И МОНИТОРИНГ", "SAFETY & MONITORING"),
            ["settings.restore"] = ("Точка восстановления", "Restore point"),
            ["settings.restore.desc"] = ("System Restore + JSON-бэкап", "System Restore + JSON backup"),
            ["settings.monitoring"] = ("Мониторинг при запуске", "Monitoring on startup"),
            ["settings.interval"] = ("Интервал, мс", "Interval, ms"),
            ["settings.game.auto"] = ("Авто-профиль при играх", "Auto-profile for games"),
            ["settings.game.auto.desc"] = ("Отслеживание процессов (WMI)", "Process tracking (WMI)"),
            ["settings.tray"] = ("ТРЕЙ И ТАЙМЕР", "TRAY & TIMER"),
            ["settings.tray.min"] = ("Сворачивать в трей", "Minimize to tray"),
            ["settings.tray.release"] = ("Отпускать таймер при выходе", "Release timer on exit"),
            ["settings.backups.max"] = ("Макс. бэкапов", "Max backups"),
            ["settings.save"] = ("СОХРАНИТЬ", "SAVE SETTINGS"),
            ["settings.saved"] = ("Настройки сохранены", "Settings saved"),
            ["settings.theme.dark"] = ("Тёмная", "Dark"),
            ["settings.theme.light"] = ("Светлая", "Light"),
            ["settings.theme.system"] = ("Как в Windows", "Match Windows"),

            ["tray.open"] = ("Открыть", "Open"),
            ["tray.reset"] = ("Сбросить оптимизации", "Reset optimizations"),
            ["tray.exit"] = ("Выход", "Exit"),
            ["tray.tooltip"] = ("AntiLag Next", "AntiLag Next"),
            ["tray.balloon.title"] = ("AntiLag Next", "AntiLag Next"),
            ["tray.balloon.text"] = (
                "Работает в трее. Таймер удерживается этим процессом.",
                "Running in tray. Timer is held by this process."),
            ["tray.exit.confirm.release"] = ("Выйти и отпустить таймер?", "Exit and release timer?"),
            ["tray.exit.confirm.keep"] = ("Выйти? Таймер останется до закрытия процесса.", "Exit? Timer remains until process ends."),
        };
        return d;
    }
}
