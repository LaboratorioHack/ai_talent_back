namespace AiTalent.Infrastructure.Email;

/// <summary>
/// SMTP settings, bound from the <c>Smtp</c> section (env vars <c>Smtp__Host</c>, <c>Smtp__Password</c>, ...).
/// Defaults target Gmail: smtp.gmail.com:587 with STARTTLS. Gmail needs an App Password, not the account password.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "smtp.gmail.com";

    public int Port { get; set; } = 587;

    /// <summary>true: STARTTLS on 587. Port 465 always uses implicit TLS.</summary>
    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    /// <summary>Gmail App Password (16 characters). Never commit it.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Sender address. Falls back to <see cref="Username"/> when empty.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Criba AI";

    public int TimeoutSeconds { get; set; } = 30;

    public string ResolvedFromAddress => string.IsNullOrWhiteSpace(FromAddress) ? Username : FromAddress;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(ResolvedFromAddress);
}
