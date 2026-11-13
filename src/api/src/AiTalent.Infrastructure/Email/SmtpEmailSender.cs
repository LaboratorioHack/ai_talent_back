using AiTalent.Application.Abstractions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AiTalent.Infrastructure.Email;

/// <summary>Sends email over SMTP with MailKit (System.Net.Mail.SmtpClient is obsolete for new code).</summary>
internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_options.IsConfigured)
        {
            logger.LogWarning("Email to {Recipient} skipped: SMTP is not configured", MaskEmail(message.To));
            throw new InvalidOperationException(
                "SMTP is not configured. Set Smtp__Username, Smtp__Password and Smtp__FromAddress.");
        }

        var mime = BuildMessage(_options, message);

        using var client = new SmtpClient { Timeout = _options.TimeoutSeconds * 1000 };
        var security = _options.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, security, ct);
            await client.AuthenticateAsync(_options.Username, _options.Password, ct);
            await client.SendAsync(new SendRequest(mime), ct);
            await client.DisconnectAsync(quit: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Callers decide whether a failed e-mail matters; the reason must still be visible to operators.
            logger.LogWarning(ex, "Email to {Recipient} via {Host}:{Port} failed: {Reason}", MaskEmail(message.To), _options.Host, _options.Port, ex.Message);
            throw;
        }

        // OWASP A09: log the event, never the body or credentials.
        logger.LogInformation("Email sent to {Recipient} with subject {Subject}", MaskEmail(message.To), message.Subject);
    }

    internal static MimeMessage BuildMessage(SmtpOptions options, EmailMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.To))
        {
            throw new ArgumentException("Recipient is required.", nameof(message));
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.ResolvedFromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.To));
        mime.Subject = message.Subject;

        var body = new BodyBuilder { HtmlBody = message.HtmlBody };
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            body.TextBody = message.TextBody;
        }

        mime.Body = body.ToMessageBody();
        return mime;
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? "***" : $"{email[0]}***{email[at..]}";
    }
}
