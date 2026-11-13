using AiTalent.Application.Abstractions;
using AiTalent.Application.Abstractions.AiEngine;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;

namespace AiTalent.Application.Features.RoleProfiles;

public sealed record ExistingRequirementInput(string? Description, string? Type, decimal? Weight);

/// <summary>
/// AI help for the role-profile editor: proposes requirements from the job description and suggests
/// equivalences for one requirement. Nothing is saved; the recruiter reviews and saves the profile.
/// </summary>
public sealed class RoleProfileAssistService(IAiEngineClient engine, StaffAccess access, IAuditLog audit)
{
    public const int MinDescriptionLength = 40;
    public const int MaxDescriptionLength = 4000;
    public const int MaxTitleLength = 200;
    public const int MaxAreaLength = 120;
    public const int MaxRequirementLength = 300;
    public const int MaxExistingRequirements = 50;
    public const int MaxLabelLength = 80;
    public const int MaxExistingLabels = 20;

    private static readonly string[] RequirementTypes = ["MANDATORY", "DESIRABLE", "DISQUALIFYING"];

    public async Task<Result<ProposeRequirementsResponse>> ProposeAsync(
        Actor actor,
        string? description,
        string? title,
        string? area,
        IReadOnlyList<ExistingRequirementInput>? existing,
        CancellationToken ct = default)
    {
        var cleanDescription = description?.Trim() ?? string.Empty;
        if (cleanDescription.Length < MinDescriptionLength || cleanDescription.Length > MaxDescriptionLength)
        {
            return Result<ProposeRequirementsResponse>.Failure(
                ErrorKind.Invalid,
                $"description must be between {MinDescriptionLength} and {MaxDescriptionLength} characters.");
        }

        var cleanTitle = Optional(title, MaxTitleLength);
        var cleanArea = Optional(area, MaxAreaLength);
        if (cleanTitle.Invalid || cleanArea.Invalid)
        {
            return Result<ProposeRequirementsResponse>.Failure(
                ErrorKind.Invalid,
                $"title allows up to {MaxTitleLength} characters and area up to {MaxAreaLength}.");
        }

        var requirements = new List<ExistingRequirementDto>();
        foreach (var item in existing ?? [])
        {
            var text = item.Description?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var type = string.IsNullOrWhiteSpace(item.Type) ? "MANDATORY" : item.Type.Trim().ToUpperInvariant();
            var weight = item.Weight ?? 0m;
            if (text.Length > MaxRequirementLength || !RequirementTypes.Contains(type) || weight < 0 || weight > 100)
            {
                return Result<ProposeRequirementsResponse>.Failure(
                    ErrorKind.Invalid,
                    $"Each existing requirement needs a description of up to {MaxRequirementLength} characters, a type ({string.Join(", ", RequirementTypes)}) and a weight from 0 to 100.");
            }

            requirements.Add(new ExistingRequirementDto(text, type, (int)Math.Round(weight)));
        }

        if (requirements.Count > MaxExistingRequirements)
        {
            return Result<ProposeRequirementsResponse>.Failure(
                ErrorKind.Invalid,
                $"At most {MaxExistingRequirements} existing requirements can be sent.");
        }

        var member = await access.GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return Result<ProposeRequirementsResponse>.Failure(member.Error!);
        }

        var call = await EngineCall.RunAsync(
            () => engine.ProposeRequirementsAsync(
                new ProposeRequirementsRequest(cleanDescription, cleanTitle.Value, cleanArea.Value, requirements),
                ct),
            "The AI engine could not propose requirements for this description.",
            ct);
        if (!call.IsSuccess)
        {
            return call;
        }

        await audit.RecordAsync(
            actor.UserId,
            "role_profile.requirements_proposed",
            "company",
            member.Value!.CompanyId.ToString(),
            new { descriptionLength = cleanDescription.Length, existing = requirements.Count, proposed = call.Value!.Requirements.Count },
            ct);

        return call;
    }

    public async Task<Result<SuggestEquivalencesResponse>> SuggestEquivalencesAsync(
        Actor actor,
        string? requirement,
        string? title,
        string? description,
        IReadOnlyList<string>? existing,
        CancellationToken ct = default)
    {
        var cleanRequirement = requirement?.Trim() ?? string.Empty;
        if (cleanRequirement.Length < 2 || cleanRequirement.Length > MaxRequirementLength)
        {
            return Result<SuggestEquivalencesResponse>.Failure(
                ErrorKind.Invalid,
                $"requirement must be between 2 and {MaxRequirementLength} characters.");
        }

        var cleanTitle = Optional(title, MaxTitleLength);
        var cleanDescription = Optional(description, MaxDescriptionLength);
        var labels = (existing ?? []).Select(label => label?.Trim() ?? string.Empty).Where(label => label.Length > 0).ToList();
        if (cleanTitle.Invalid || cleanDescription.Invalid || labels.Count > MaxExistingLabels || labels.Any(label => label.Length > MaxLabelLength))
        {
            return Result<SuggestEquivalencesResponse>.Failure(
                ErrorKind.Invalid,
                $"title allows up to {MaxTitleLength} characters, description up to {MaxDescriptionLength}, and up to {MaxExistingLabels} existing labels of {MaxLabelLength} characters.");
        }

        var member = await access.GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return Result<SuggestEquivalencesResponse>.Failure(member.Error!);
        }

        var call = await EngineCall.RunAsync(
            () => engine.SuggestEquivalencesAsync(
                new SuggestEquivalencesRequest(cleanRequirement, cleanTitle.Value, cleanDescription.Value, labels),
                ct),
            "The AI engine could not suggest equivalences for this requirement.",
            ct);
        if (!call.IsSuccess)
        {
            return call;
        }

        await audit.RecordAsync(
            actor.UserId,
            "role_profile.equivalences_suggested",
            "company",
            member.Value!.CompanyId.ToString(),
            new { suggested = call.Value!.Equivalences.Count },
            ct);

        return call;
    }

    private static (string? Value, bool Invalid) Optional(string? value, int maxLength)
    {
        var clean = value?.Trim();
        if (string.IsNullOrEmpty(clean))
        {
            return (null, false);
        }

        return clean.Length > maxLength ? (null, true) : (clean, false);
    }
}
