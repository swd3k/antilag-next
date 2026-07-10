using System.Text.Json;
using System.Text.Json.Serialization;

namespace AntiLagNext.Infrastructure.Storage;

/// <summary>
/// Простейший JSON-репозиторий с атомарной записью (.tmp + File.Move).
/// Единые настройки сериализации для всего приложения.
/// </summary>
public static class JsonStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Сохранить объект в файл атомарно: write → .tmp, then File.Replace / Move.
    /// Never delete the target before the write succeeds (crash-safe).
    /// </summary>
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        {
            JsonSerializer.Serialize(fs, value, Options);
            fs.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            // Atomic replace on same volume; no window where target is missing
            File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    /// <summary>Загрузить объект из файла; null/default если файла нет.</summary>
    public static T? Load<T>(string path)
    {
        if (!File.Exists(path)) return default;
        using var fs = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(fs, Options);
    }

    /// <summary>Сериализовать в строку (для логов/UI).</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
