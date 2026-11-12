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

    public void Add(ApplicationEntity application) => db.Applications.Add(application);

    public void Remove(ApplicationEntity application) => db.Applications.Remove(application);
}
