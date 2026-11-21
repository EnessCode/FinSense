using FinSense.Domain.Enums;

namespace FinSense.Application.Features.Categories.Contracts;

/// <summary>
/// <see cref="Name"/> is localized according to the caller's locale (<c>Accept-Language</c>):
/// <c>en-US</c> → <c>name_en</c> (falls back to <c>name_tr</c>), otherwise <c>name_tr</c>.
/// </summary>
public sealed record CategoryDto(
    Guid Id,
    string Name,
    string NameTr,
    string? NameEn,
    CategoryType Type,
    string Icon,
    string ColorHex,
    bool IsSystem);

internal static class CategoryLocalization
{
    public static bool IsEnglish(string locale) =>
        string.Equals(locale, "en-US", StringComparison.OrdinalIgnoreCase);
}
