namespace AntiLagNext.Core.Models;

/// <summary>
/// Результат операции оптимизации без возвращаемого значения.
/// Единый тип для всех сервисов: успех/провал, понятное сообщение, детали, исключение.
/// </summary>
public sealed class OperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public Exception? Exception { get; init; }

    public static OperationResult Ok(string message = "Операция выполнена успешно.")
        => new() { Success = true, Message = message };

    public static OperationResult Fail(string message, string? detail = null, Exception? ex = null)
        => new() { Success = false, Message = message, Detail = detail, Exception = ex };

    public override string ToString() => Success ? Message : $"ОШИБКА: {Message}";
}

/// <summary>
/// Результат операции со значением.
/// Без ограничения where T : class — поддерживает Guid, uint и другие value types.
/// </summary>
/// <typeparam name="T">Тип полезной нагрузки (class или struct).</typeparam>
public sealed class OperationResult<T>
{
    public bool Success { get; init; }
    public T? Value { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public Exception? Exception { get; init; }

    public static OperationResult<T> Ok(T value, string message = "")
        => new() { Success = true, Value = value, Message = message };

    public static OperationResult<T> Fail(string message, string? detail = null, Exception? ex = null)
        => new() { Success = false, Message = message, Detail = detail, Exception = ex };

    public override string ToString() => Success ? Message : $"ОШИБКА: {Message}";
}
