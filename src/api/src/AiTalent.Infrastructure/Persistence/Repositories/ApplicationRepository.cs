using AiTalent.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using ApplicationEntity = AiTalent.Domain.Entities.Application;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class ApplicationRepository(AppDbContext db) : IApplicationRepository
{
    public Task<ApplicationEntity?> FindByJobAndCandidateAsync(Guid jobOpeningId, Guid candidateId, CancellationToken ct = default) =>
        db.Applications.FirstOrDefaultAsync(
            application => application.JobOpeningId == jobOpeningId && application.CandidateId == candidateId,
            ct);

    public Task<ApplicationEntity?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Applications.FirstOrDefaultAsync(application => application.Id == id, ct);

    public async Task<IReadOnlyList<ApplicationSubmission>> ListByJobOpeningAsync(Guid jobOpeningId, CancellationToken ct = default)
    {
        var rows = await (
            from application in db.Applications
            where application.JobOpeningId == jobOpeningId
            join candidate in db.Candidates on application.CandidateId equals candidate.Id
            orderby application.CreatedAt descending
            select new { Application = application, Candidate = candidate })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var candidateIds = rows.Select(row => row.Candidate.Id).Distinct().ToList();
        var latestResumes = await db.Resumes
            .Where(resume => candidateIds.Contains(resume.CandidateId))
            .GroupBy(resume => resume.CandidateId)
            .Select(group => group.OrderByDescending(resume => resume.Version).ThenByDescending(resume => resume.CreatedAt).First())
            .ToListAsync(ct);

        var resumeByCandidate = latestResumes.ToDictionary(resume => resume.CandidateId);

        return rows.Select(row =>
        {
            resumeByCandidate.TryGetValue(row.Candidate.Id, out var resume);
            return new ApplicationSubmission(
                row.Application.Id,
                row.Candidate.Id,
                row.Candidate.Name,
                row.Candidate.ContactEmail,
                row.Application.Source.ToString().ToUpperInvariant(),
                row.Application.SelectionStatus.ToString().ToUpperInvariant(),
                row.Application.CreatedAt,
                row.Application.UpdatedAt,
                resume?.Id,
                resume?.Version,
                resume?.ProcessingStatus.ToString().ToUpperInvariant(),
                resume?.ProcessingError,
                resume?.CreatedAt);
        }).ToList();
    }

    public void Add(ApplicationEntity application) => db.Applications.Add(application);

    public void Remove(ApplicationEntity application) => db.Applications.Remove(application);
}
