using System.Diagnostics.CodeAnalysis;

namespace AiTalent.Application.Common;

internal static class Validation
{
    public const int MaxNameLength = 120;
    public const int MinSectorLength = 2;
    public const int MaxSectorLength = 80;

    /// <summary>Canonical organization sizes accepted by the API and enforced by a DB check constraint.</summary>
    public static readonly IReadOnlyList<string> CompanySizes =
        ["1-10", "11-50", "51-200", "201-500", "501-1000", "1000+"];

    public static bool IsName([NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaxNameLength;

    public static bool IsSector([NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length is >= MinSectorLength and <= MaxSectorLength;

    public static bool IsCompanySize([NotNullWhen(true)] string? value) =>
        value is not null && CompanySizes.Contains(value, StringComparer.Ordinal);
}
