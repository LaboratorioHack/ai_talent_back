using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Domain.Entities;

namespace AiTalent.Application.Features.Vacancies;

public sealed record PublicVacancy(JobOpening Vacancy, string CompanyName, string? CompanySector = null, string? CompanySize = null);

/// <summary>Read-only view of an open vacancy for candidates. Never exposes scoring internals.</summary>
public sealed class PublicVacancyService(
    IJobOpeningRepository vacancies,
    ICompanyRepository companies)
{
    public async Task<Result<PublicVacancy>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var vacancy = await vacancies.FindPublicAsync(id, ct);
        if (vacancy is null || !vacancy.IsAcceptingApplications(DateTimeOffset.UtcNow))
        {
            return Result<PublicVacancy>.Failure(ErrorKind.NotFound, "Vacancy not found.");
        }

        var company = await companies.FindByIdAsync(vacancy.CompanyId, ct);
        if (company is null)
        {
            return Result<PublicVacancy>.Failure(ErrorKind.NotFound, "Vacancy not found.");
        }

        return Result<PublicVacancy>.Success(new PublicVacancy(vacancy, company.Name, company.Sector, company.Size));
    }
}
