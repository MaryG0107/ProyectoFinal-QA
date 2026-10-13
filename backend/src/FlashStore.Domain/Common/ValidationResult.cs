namespace FlashStore.Domain.Common;

/// <summary>
/// Resultado de una validación de negocio: contiene el valor válido o el mensaje de error.
/// </summary>
public sealed class ValidationResult<T>
{
    private ValidationResult(bool isValid, T? value, string? error)
    {
        IsValid = isValid;
        Value = value;
        Error = error;
    }

    public bool IsValid { get; }

    public T? Value { get; }

    public string? Error { get; }

    public static ValidationResult<T> Success(T value) => new(true, value, null);

    public static ValidationResult<T> Failure(string error) => new(false, default, error);
}
