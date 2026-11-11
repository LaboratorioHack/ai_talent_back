using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;

namespace AiTalent.Application.Features.Auth;

public sealed record Me(Guid UserId, bool MfaVerified, Membership? Membership);

public sealed class AuthService(IStaffRepository staff, ICompanyRepository companies)
{
    public async Task<Result<Me>> GetMeAsync(Actor actor, CancellationToken ct = default)
    {
        var member = await staff.FindByUserIdAsync(actor.UserId, ct);
        if (member is null)
        {
            return Result<Me>.Success(new Me(actor.UserId, actor.MfaVerified, null));
        }

        if (!member.Active)
        {
            return Result<Me>.Failure(ErrorKind.Forbidden, "Your account is deactivated.");
        }

        var company = await companies.FindByIdAsync(member.CompanyId, ct);
        if (company is null)
        {
            return Result<Me>.Failure(ErrorKind.NotFound, "Company not found.");
        }

        var membership = new Membership(company.Id, company.Name, member.Role);
        return Result<Me>.Success(new Me(actor.UserId, actor.MfaVerified, membership));
    }
}
