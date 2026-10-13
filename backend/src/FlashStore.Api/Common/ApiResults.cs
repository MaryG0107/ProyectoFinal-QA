using System.Net.Mail;

namespace FlashStore.Api.Common;

/// <summary>Formato único de error para toda la API: { "error": "mensaje" }.</summary>
public static class ApiResults
{
    public static IResult Error(int statusCode, string message) =>
        Results.Json(new { error = message }, statusCode: statusCode);
}

public static class InputValidation
{
    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= 150
        && MailAddress.TryCreate(email, out var parsed)
        && string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase);

    /// <summary>Mínimo 8 caracteres, máximo 72 (límite de BCrypt), al menos una letra y un número.</summary>
    public static bool IsValidPassword(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length is >= 8 and <= 72
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 2 and <= 150;
}
