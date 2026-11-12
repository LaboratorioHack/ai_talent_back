using AiTalent.Application.Abstractions;
using AiTalent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class ResumeRepository(AppDbContext db) : IResumeRepository
{
    public async Task<int> GetNextVersionAsync(Guid candidateId, CancellationToken ct = default)
    {
        var max = await db.Resumes
            .Where(resume => resume.CandidateId == candidateId)
            .Select(resume => (int?)resume.Version)
            .MaxAsync(ct);

        return (max ?? 0) + 1;
    }

    public Task<bool> ExistsWithHashAsync(Guid candidateId, string fileHash, CancellationToken ct = default) =>
        db.Resumes.AnyAsync(resume => resume.CandidateId == candidateId && resume.FileHash == fileHash, ct);

    public Task<Resume?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Resumes.FirstOrDefaultAsync(resume => resume.Id == id, ct);

    public void Add(Resume resume) => db.Resumes.Add(resume);

    public void Remove(Resume resume) => db.Resumes.Remove(resume);
}
