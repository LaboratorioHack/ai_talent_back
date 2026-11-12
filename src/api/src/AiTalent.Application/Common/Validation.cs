using System.Diagnostics.CodeAnalysis;

namespace AiTalent.Application.Common;

internal static class Validation
{
    public const int MaxNameLength = 120;

    public static bool IsName([NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaxNameLength;
}
