using System.Diagnostics.CodeAnalysis;

namespace AiTalent.Application.Common;

internal static class Validation
{
    public const int MaxNameLength = 120;
    public const int MinSectorLength = 2;
    public const int MaxSectorLength = 80;
    public const int MaxAreaLength = 80;
    public const int MaxDescriptionLength = 4000;
    public const int MaxRequirementDescriptionLength = 1000;
    public const int MaxRequirementConditionLength = 200;
    public const int MaxEquivalenceNameLength = 80;
    public const int MaxPositions = 1000;
    public const int MaxStageReasonLength = 500;

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

    public static bool IsOptionalArea(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length is >= 2 and <= MaxAreaLength;

    public static bool IsEmail([NotNullWhen(true)] string? value) =>
        value is { Length: >= 5 and <= 254 }
        && value.IndexOf('@') is > 0 and var at
        && value.IndexOf('.', at) > at + 1
        && !value.Any(char.IsWhiteSpace);

    public static bool IsPositions(int? value) => value is >= 1 and <= MaxPositions;

    public static bool IsClosingAt(DateTimeOffset? value) =>
        value is null || value.Value > DateTimeOffset.UtcNow;

    public static bool IsWeight(decimal? value) => value is >= 0 and <= 100;

    public static bool IsText(string? value, int min, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= min && value.Trim().Length <= max;

    public static bool IsOptionalText(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length <= max;

    public static bool IsDescription(string? value) =>
        value is null || value.Trim().Length <= MaxDescriptionLength;
}
