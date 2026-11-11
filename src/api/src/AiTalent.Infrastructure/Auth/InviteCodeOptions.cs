namespace AiTalent.Infrastructure.Auth;

public sealed class InviteCodeOptions
{
    public const string SectionName = "Invites";

    /// <summary>Secreto HMAC para firmar los códigos de invitación. Mínimo 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(7);
}
