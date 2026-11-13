using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.Application.Features.Companies;

public sealed class CompanyService(
    ICompanyRepository companies,
    IStaffRepository staff,
    IApplicationRepository applications,
    StaffAccess access,
    IInviteCodes inviteCodes,
    IAuditLog audit,
    IUnitOfWork unitOfWork)
{
    // Same message for every failure so an attacker can't tell expired from tampered codes.
    private const string InvalidCodeMessage = "The invite code is invalid or has expired.";

    // An account is either an applicant or company staff, never both: nobody evaluates their own application.
    private const string ApplicantAccountMessage = "This account already applied to vacancies as an applicant. Use another e-mail for a company account.";

    public async Task<Result<Membership>> CreateAsync(
        Guid userId,
        string? companyName,
        string? sector,
        string? size,
        string? yourName,
        string? email = null,
        CancellationToken ct = default)
    {
        if (!Validation.IsName(companyName) || !Validation.IsName(yourName))
        {
            return Result<Membership>.Failure(ErrorKind.Invalid, "Company name and your name are required (max 120 characters).");
        }

        if (!Validation.IsSector(sector))
        {
            return Result<Membership>.Failure(
                ErrorKind.Invalid,
                $"Sector is required (2 to {Validation.MaxSectorLength} characters).");
        }

        var companySize = size?.Trim();
        if (!Validation.IsCompanySize(companySize))
        {
            return Result<Membership>.Failure(
                ErrorKind.Invalid,
                $"Size must be one of: {string.Join(", ", Validation.CompanySizes)}.");
        }

        if (await staff.FindByUserIdAsync(userId, ct) is not null)
        {
            return Result<Membership>.Failure(ErrorKind.Conflict, "You already belong to a company.");
        }

        if (await applications.HasSelfSubmittedAsync(userId, NormalizeEmail(email), ct))
        {
            return Result<Membership>.Failure(ErrorKind.Forbidden, ApplicantAccountMessage);
        }

        var company = new Company(companyName.Trim(), sector.Trim(), companySize);
        staff.Add(new StaffMember(userId, company.Id, yourName.Trim(), StaffRole.Admin));
        companies.Add(company);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DuplicateEntityException)
        {
            // A second request from the same user won the race. Both rows are saved together, so nothing is left behind.
            return Result<Membership>.Failure(ErrorKind.Conflict, "You already belong to a company.");
        }

        await audit.RecordAsync(userId, "company.created", "company", company.Id.ToString(), new { }, ct);
        return Result<Membership>.Success(
            new Membership(company.Id, company.Name, company.Sector, company.Size, StaffRole.Admin));
    }

    public async Task<Result<InviteCode>> CreateInviteCodeAsync(Actor actor, CancellationToken ct = default)
    {
        var admin = await access.GetAdminAsync(actor, ct);
        if (!admin.IsSuccess)
        {
            return Result<InviteCode>.Failure(admin.Error!);
        }

        var companyId = admin.Value!.CompanyId;
        var invite = inviteCodes.Issue(companyId, StaffRole.Recruiter);

        await audit.RecordAsync(
            actor.UserId,
            "invite_code.created",
            "company",
            companyId.ToString(),
            new { role = "RECRUITER", expiresAt = invite.ExpiresAt },
            ct);

        return Result<InviteCode>.Success(invite);
    }

    public async Task<Result<Membership>> JoinAsync(Guid userId, string? code, string? yourName, string? email = null, CancellationToken ct = default)
    {
        if (!Validation.IsName(yourName))
        {
            return Result<Membership>.Failure(ErrorKind.Invalid, "Your name is required (max 120 characters).");
        }

        if (await staff.FindByUserIdAsync(userId, ct) is not null)
        {
            return Result<Membership>.Failure(ErrorKind.Conflict, "You already belong to a company.");
        }

        if (await applications.HasSelfSubmittedAsync(userId, NormalizeEmail(email), ct))
        {
            return Result<Membership>.Failure(ErrorKind.Forbidden, ApplicantAccountMessage);
        }

        var claims = code is null ? null : await inviteCodes.ReadAsync(code.Trim(), ct);
        if (claims is null)
        {
            return Result<Membership>.Failure(ErrorKind.Invalid, InvalidCodeMessage);
        }

        var company = await companies.FindByIdAsync(claims.CompanyId, ct);
        if (company is null)
        {
            return Result<Membership>.Failure(ErrorKind.Invalid, InvalidCodeMessage);
        }

        staff.Add(new StaffMember(userId, company.Id, yourName.Trim(), claims.Role));
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DuplicateEntityException)
        {
            return Result<Membership>.Failure(ErrorKind.Conflict, "You already belong to a company.");
        }

        await audit.RecordAsync(
            userId,
            "staff.joined",
            "staff",
            userId.ToString(),
            new { companyId = company.Id, role = claims.Role.ToString().ToUpperInvariant() },
            ct);

        return Result<Membership>.Success(
            new Membership(company.Id, company.Name, company.Sector, company.Size, claims.Role));
    }

    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
