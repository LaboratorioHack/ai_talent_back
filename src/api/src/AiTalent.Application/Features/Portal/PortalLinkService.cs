using System.Net;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Interviews;

namespace AiTalent.Application.Features.Portal;

/// <summary>
/// Sends the applicant portal magic link through Criba's SMTP. The link points to the web app's
/// <c>/auth/confirm</c>, which verifies the one-time token with Supabase and opens the session.
/// The response never says whether the e-mail already had an account.
/// </summary>
public sealed class PortalLinkService(IAuthAdmin auth, IEmailSender email)
{
    public async Task<Result<bool>> SendAsync(string? address, string appBaseUrl, CancellationToken ct = default)
    {
        var to = address?.Trim().ToLowerInvariant();
        if (!Validation.IsEmail(to))
        {
            return Result<bool>.Failure(ErrorKind.Invalid, "A valid e-mail is required.");
        }

        var token = await auth.CreateMagicLinkTokenAsync(to, ct);
        if (token is null)
        {
            return Result<bool>.Failure(ErrorKind.Unavailable, "The sign-in link could not be created.");
        }

        var link = $"{appBaseUrl.TrimEnd('/')}/auth/confirm?token_hash={WebUtility.UrlEncode(token)}&type=magiclink&next=%2Fportal";
        try
        {
            await email.SendAsync(new EmailMessage(
                to,
                "Tu enlace para entrar a Criba",
                CandidateInterviewService.Html(
                    "Hola",
                    "Entra a tu portal de postulante para ver en qué va cada postulación. El enlace sirve una sola vez y vence en una hora.",
                    link,
                    "Entrar a mi portal",
                    "Si no lo pediste, ignora este correo."),
                $"Entra a tu portal de postulante con este enlace (sirve una vez y vence en una hora): {link}"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<bool>.Failure(ErrorKind.Unavailable, "The e-mail could not be sent.");
        }

        return Result<bool>.Success(true);
    }
}
