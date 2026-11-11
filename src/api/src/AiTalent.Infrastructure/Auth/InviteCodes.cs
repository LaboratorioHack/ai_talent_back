using System.Text;
using AiTalent.Application.Abstractions;
using AiTalent.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AiTalent.Infrastructure.Auth;

public sealed class InviteCodes : IInviteCodes
{
    private const string Issuer = "ai-talent-api";
    private const string Audience = "company-invite";
    private const string TokenType = "company-invite";
    private const string CompanyIdClaim = "company_id";
    private const string RoleClaim = "role";

    private static readonly JsonWebTokenHandler Handler = new();

    private readonly SigningCredentials _credentials;
    private readonly TokenValidationParameters _validation;
    private readonly TimeSpan _lifetime;

    public InviteCodes(IOptions<InviteCodeOptions> options)
    {
        var value = options.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(value.SigningKey));

        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _lifetime = value.Lifetime;
        _validation = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            ValidTypes = [TokenType],
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = key,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    public InviteCode Issue(Guid companyId, StaffRole role)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(_lifetime);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            TokenType = TokenType,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>
            {
                [CompanyIdClaim] = companyId.ToString(),
                [RoleClaim] = role.ToString().ToUpperInvariant(),
            },
        };

        return new InviteCode(Handler.CreateToken(descriptor), expiresAt);
    }

    public async Task<InviteClaims?> ReadAsync(string code, CancellationToken ct = default)
    {
        var result = await Handler.ValidateTokenAsync(code, _validation);
        if (!result.IsValid)
        {
            return null;
        }

        var companyId = result.ClaimsIdentity.FindFirst(CompanyIdClaim)?.Value;
        var role = result.ClaimsIdentity.FindFirst(RoleClaim)?.Value;
        if (!Guid.TryParse(companyId, out var id)
            || !Enum.TryParse(role, ignoreCase: true, out StaffRole parsedRole)
            || !Enum.IsDefined(parsedRole))
        {
            return null;
        }

        return new InviteClaims(id, parsedRole);
    }
}
