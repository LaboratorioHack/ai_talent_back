namespace AiTalent.Application.Abstractions;

/// <summary>A single outgoing email. <see cref="TextBody"/> is the plain-text fallback for clients that do not render HTML.</summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? TextBody = null,
    string? ToName = null);

/// <summary>Sends transactional emails (invites, notifications) through the configured SMTP account.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
