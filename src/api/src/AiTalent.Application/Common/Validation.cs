using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using System.Text;

namespace AiTalent.Application.Common;

internal static class Validation
{
    public const int MaxNameLength = 120;
    public const int MinPasswordLength = 12;
    public const int MaxPasswordBytes = 72;

    public static bool IsName([NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaxNameLength;

    public static bool IsEmail([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254)
        {
            return false;
        }

        var trimmed = value.Trim();
        try
        {
            return new MailAddress(trimmed).Address == trimmed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsPassword([NotNullWhen(true)] string? value) =>
        value is not null
        && value.Length >= MinPasswordLength
        && Encoding.UTF8.GetByteCount(value) <= MaxPasswordBytes;
}
