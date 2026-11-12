using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.Application.Features.Vacancies;

public sealed record RequirementInput(
    RequirementType Type,
    string? Category,
    string? Description,
    string? Condition,
    decimal? Weight,
    IReadOnlyDictionary<string, int>? Equivalences);

public sealed record ProfileInput(string? Description, IReadOnlyList<RequirementInput>? Requirements);

public sealed record VacancyPage(IReadOnlyList<JobOpening> Items, int Page, int PageSize, int Total);

public sealed record VacancyDetail(JobOpening Vacancy, bool ProfileInUse);

public sealed class VacancyService(
    IJobOpeningRepository vacancies,
    StaffAccess access,
    IAuditLog audit,
    IUnitOfWork unitOfWork)
{
    private const decimal RequiredWeightTotal = 100m;

    public async Task<Result<VacancyPage>> ListAsync(
        Actor actor,
        JobOpeningStatus? status,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return Result<VacancyPage>.Failure(member.Error!);
        }

        var currentPage = page is null or < 1 ? 1 : page.Value;
        var currentPageSize = pageSize is null or < 1 or > 100 ? 20 : pageSize.Value;
        var companyId = member.Value!.CompanyId;

        var items = await vacancies.ListByCompanyAsync(
            companyId,
            status,
            search,
            (currentPage - 1) * currentPageSize,
            currentPageSize,
            ct);
        var total = await vacancies.CountByCompanyAsync(companyId, status, search, ct);

        return Result<VacancyPage>.Success(new VacancyPage(items, currentPage, currentPageSize, total));
    }

    public async Task<Result<VacancyDetail>> GetAsync(Guid actorUserId, Guid id, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<VacancyDetail>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, id, ct);
        if (vacancy is null)
        {
            return NotFound();
        }

        return Result<VacancyDetail>.Success(await DetailAsync(vacancy, ct));
    }

    public async Task<Result<VacancyDetail>> CreateAsync(
        Actor actor,
        string? title,
        string? area,
        DateTimeOffset? closingAt,
        int? positions,
        ProfileInput? profile,
        CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return Result<VacancyDetail>.Failure(member.Error!);
        }

        var metadataError = ValidateMetadata(title, area, closingAt, positions, out var cleanTitle, out var cleanArea, out var cleanPositions);
        if (metadataError is not null)
        {
            return Invalid(metadataError);
        }

        var profileError = ValidateProfile(profile);
        if (profileError is not null)
        {
            return Invalid(profileError);
        }

        var vacancy = new JobOpening(
            member.Value!.CompanyId,
            actor.UserId,
            cleanTitle,
            cleanArea,
            closingAt?.ToUniversalTime(),
            cleanPositions);

        if (profile is not null)
        {
            var roleProfile = new RoleProfile(vacancy.Id, 1, Normalize(profile.Description));
            roleProfile.ReplaceRequirements(BuildRequirements(roleProfile.Id, profile.Requirements));
            vacancy.Profiles.Add(roleProfile);
        }

        vacancies.Add(vacancy);
        await unitOfWork.SaveChangesAsync(ct);
        await audit.RecordAsync(
            actor.UserId,
            "vacancy.created",
            "job_opening",
            vacancy.Id.ToString(),
            new { title = vacancy.Title, area = vacancy.Area },
            ct);

        return Result<VacancyDetail>.Success(await DetailAsync(vacancy, ct));
    }

    public async Task<Result<VacancyDetail>> UpdateAsync(
        Actor actor,
        Guid id,
        string? title,
        string? area,
        DateTimeOffset? closingAt,
        int? positions,
        CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return Result<VacancyDetail>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, id, ct);
        if (vacancy is null)
        {
            return NotFound();
        }

        var metadataError = ValidateMetadata(title, area, closingAt, positions, out var cleanTitle, out var cleanArea, out var cleanPositions);
        if (metadataError is not null)
        {
            return Invalid(metadataError);
        }

        vacancy.Update(cleanTitle, cleanArea, closingAt?.ToUniversalTime(), cleanPositions);
        await unitOfWork.SaveChangesAsync(ct);
        await audit.RecordAsync(
            actor.UserId,
            "vacancy.updated",
            "job_opening",
            vacancy.Id.ToString(),
            new { title = vacancy.Title, area = vacancy.Area, positions = vacancy.Positions },
            ct);

        return Result<VacancyDetail>.Success(await DetailAsync(vacancy, ct));
    }

    public async Task<Result<VacancyDetail>> PublishAsync(Guid actorUserId, Guid id, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<VacancyDetail>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, id, ct);
        if (vacancy is null)
        {
            return NotFound();
        }

        if (vacancy.Status != JobOpeningStatus.Draft)
        {
            return Invalid("Only draft vacancies can be published.");
        }

        var profile = vacancy.CurrentProfile;
        if (profile is null || profile.Requirements.Count == 0)
        {
            return Invalid("Add at least one requirement before publishing.");
        }

        if (Math.Abs(profile.WeightTotal - RequiredWeightTotal) > 0.01m)
        {
            return Invalid($"Requirement weights must add up to 100 (currently {profile.WeightTotal:0.##}).");
        }

        if (vacancy.ClosingAt is { } closingAt && closingAt <= DateTimeOffset.UtcNow)
        {
            return Invalid("The closing date must be in the future.");
        }

        vacancy.Publish();
        await unitOfWork.SaveChangesAsync(ct);
        await audit.RecordAsync(
            actorUserId,
            "vacancy.published",
            "job_opening",
            vacancy.Id.ToString(),
            new { profileVersion = profile.Version },
            ct);

        return Result<VacancyDetail>.Success(await DetailAsync(vacancy, ct));
    }

    public async Task<Result<VacancyDetail>> CloseAsync(Guid actorUserId, Guid id, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<VacancyDetail>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, id, ct);
        if (vacancy is null)
        {
            return NotFound();
        }

        if (vacancy.Status != JobOpeningStatus.Open)
        {
            return Invalid("Only open vacancies can be closed.");
        }

        vacancy.Close();
        await unitOfWork.SaveChangesAsync(ct);
        await audit.RecordAsync(
            actorUserId,
            "vacancy.closed",
            "job_opening",
            vacancy.Id.ToString(),
            new { },
            ct);

        return Result<VacancyDetail>.Success(await DetailAsync(vacancy, ct));
    }

    public async Task<Result<Guid>> DeleteAsync(Guid actorUserId, Guid id, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<Guid>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, id, ct);
        if (vacancy is null)
        {
            return Result<Guid>.Failure(NotFoundError());
        }

        vacancy.Delete();
        await unitOfWork.SaveChangesAsync(ct);
        await audit.RecordAsync(
            actorUserId,
            "vacancy.deleted",
            "job_opening",
            vacancy.Id.ToString(),
            new { },
            ct);

        return Result<Guid>.Success(vacancy.Id);
    }

    public async Task<Result<VacancyDetail>> UpdateProfileAsync(
        Guid actorUserId,
        Guid id,
        ProfileInput? profile,
        CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<VacancyDetail>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, id, ct);
        if (vacancy is null)
        {
            return NotFound();
        }

        var profileError = ValidateProfile(profile);
        if (profileError is not null)
        {
            return Invalid(profileError);
        }

        var current = vacancy.CurrentProfile;
        if (current is null)
        {
            var first = new RoleProfile(vacancy.Id, 1, Normalize(profile?.Description));
            first.ReplaceRequirements(BuildRequirements(first.Id, profile?.Requirements));
            vacancy.Profiles.Add(first);
            await SaveProfileAsync(actorUserId, vacancy, first, "created", ct);
            return Result<VacancyDetail>.Success(await DetailAsync(vacancy, ct));
        }

        // A profile already used by an evaluation is immutable: changes create a new version.
        if (await vacancies.IsProfileInUseAsync(current.Id, ct))
        {
            var next = new RoleProfile(
                vacancy.Id,
                vacancy.Profiles.Max(item => item.Version) + 1,
                Normalize(profile?.Description));
            next.ReplaceRequirements(BuildRequirements(next.Id, profile?.Requirements));
            vacancy.Profiles.Add(next);
            await SaveProfileAsync(actorUserId, vacancy, next, "versioned", ct);
            return Result<VacancyDetail>.Success(await DetailAsync(vacancy, ct));
        }

        current.Update(Normalize(profile?.Description));
        current.ReplaceRequirements(BuildRequirements(current.Id, profile?.Requirements));
        await SaveProfileAsync(actorUserId, vacancy, current, "updated", ct);

        return Result<VacancyDetail>.Success(await DetailAsync(vacancy, ct));
    }

    private async Task SaveProfileAsync(
        Guid actorUserId,
        JobOpening vacancy,
        RoleProfile profile,
        string action,
        CancellationToken ct)
    {
        await unitOfWork.SaveChangesAsync(ct);
        await audit.RecordAsync(
            actorUserId,
            $"vacancy.profile.{action}",
            "role_profile",
            profile.Id.ToString(),
            new { vacancyId = vacancy.Id, profile.Version, requirements = profile.Requirements.Count },
            ct);
    }

    private async Task<VacancyDetail> DetailAsync(JobOpening vacancy, CancellationToken ct)
    {
        var profile = vacancy.CurrentProfile;
        var inUse = profile is not null && await vacancies.IsProfileInUseAsync(profile.Id, ct);
        return new VacancyDetail(vacancy, inUse);
    }

    private static string? ValidateMetadata(
        string? title,
        string? area,
        DateTimeOffset? closingAt,
        int? positions,
        out string cleanTitle,
        out string? cleanArea,
        out int cleanPositions)
    {
        cleanTitle = title?.Trim() ?? string.Empty;
        cleanArea = Normalize(area);
        cleanPositions = positions ?? 1;

        if (!Validation.IsName(title))
        {
            return $"Title is required (max {Validation.MaxNameLength} characters).";
        }

        if (!Validation.IsOptionalArea(area))
        {
            return $"Area must be between 2 and {Validation.MaxAreaLength} characters.";
        }

        if (!Validation.IsPositions(cleanPositions))
        {
            return $"Positions must be between 1 and {Validation.MaxPositions}.";
        }

        if (!Validation.IsClosingAt(closingAt))
        {
            return "The closing date must be in the future.";
        }

        return null;
    }

    private static string? ValidateProfile(ProfileInput? profile)
    {
        if (profile is null)
        {
            return null;
        }

        if (!Validation.IsDescription(profile.Description))
        {
            return $"Description must be at most {Validation.MaxDescriptionLength} characters.";
        }

        var requirements = profile.Requirements ?? Array.Empty<RequirementInput>();
        for (var index = 0; index < requirements.Count; index++)
        {
            var requirement = requirements[index];
            var position = index + 1;

            if (!Validation.IsText(
                    requirement.Description,
                    2,
                    Validation.MaxRequirementDescriptionLength))
            {
                return $"Requirement {position}: description is required (2 to {Validation.MaxRequirementDescriptionLength} characters).";
            }

            if (!Validation.IsOptionalText(requirement.Category, Validation.MaxAreaLength))
            {
                return $"Requirement {position}: category must be at most {Validation.MaxAreaLength} characters.";
            }

            if (!Validation.IsOptionalText(requirement.Condition, Validation.MaxRequirementConditionLength))
            {
                return $"Requirement {position}: condition must be at most {Validation.MaxRequirementConditionLength} characters.";
            }

            if (requirement.Weight is null || !Validation.IsWeight(requirement.Weight))
            {
                return $"Requirement {position}: weight is required (0 to 100).";
            }

            var equivalenceError = ValidateEquivalences(position, requirement.Equivalences);
            if (equivalenceError is not null)
            {
                return equivalenceError;
            }
        }

        return null;
    }

    private static string? ValidateEquivalences(int position, IReadOnlyDictionary<string, int>? equivalences)
    {
        if (equivalences is null)
        {
            return null;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, percent) in equivalences)
        {
            var cleanName = name.Trim();
            if (cleanName.Length is < 1 or > Validation.MaxEquivalenceNameLength)
            {
                return $"Requirement {position}: equivalence names must be between 1 and {Validation.MaxEquivalenceNameLength} characters.";
            }

            if (!names.Add(cleanName))
            {
                return $"Requirement {position}: equivalence '{cleanName}' is repeated.";
            }

            if (percent is < 1 or > 100)
            {
                return $"Requirement {position}: equivalence '{cleanName}' must be between 1 and 100 percent.";
            }
        }

        return null;
    }

    private static List<Requirement> BuildRequirements(
        Guid roleProfileId,
        IReadOnlyList<RequirementInput>? inputs) =>
        (inputs ?? Array.Empty<RequirementInput>())
        .Select(input => new Requirement(
            roleProfileId,
            input.Type,
            Normalize(input.Category),
            input.Description!.Trim(),
            Normalize(input.Condition),
            input.Weight!.Value,
            CleanEquivalences(input.Equivalences)))
        .ToList();

    private static Dictionary<string, int>? CleanEquivalences(IReadOnlyDictionary<string, int>? equivalences)
    {
        if (equivalences is null || equivalences.Count == 0)
        {
            return null;
        }

        return equivalences.ToDictionary(
            pair => pair.Key.Trim(),
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<VacancyDetail> Invalid(string message) =>
        Result<VacancyDetail>.Failure(ErrorKind.Invalid, message);

    private static Result<VacancyDetail> NotFound() =>
        Result<VacancyDetail>.Failure(ErrorKind.NotFound, "Vacancy not found.");

    private static AppError NotFoundError() =>
        new(ErrorKind.NotFound, "Vacancy not found.");
}
