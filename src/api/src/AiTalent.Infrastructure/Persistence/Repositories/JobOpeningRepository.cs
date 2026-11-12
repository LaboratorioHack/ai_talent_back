using AiTalent.Application.Abstractions;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class JobOpeningRepository(AppDbContext db) : IJobOpeningRepository
{
    public async Task<IReadOnlyList<JobOpening>> ListByCompanyAsync(
        Guid companyId,
        JobOpeningStatus? status,
        string? search,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var query = db.JobOpenings
            .AsNoTracking()
            .Include(opening => opening.Profiles)
            .Where(opening => opening.CompanyId == companyId && opening.DeletedAt == null);

        query = ApplyFilters(query, status, search);

        return await query
            .OrderByDescending(opening => opening.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> CountByCompanyAsync(
        Guid companyId,
        JobOpeningStatus? status,
        string? search,
        CancellationToken ct = default) =>
        await ApplyFilters(
                db.JobOpenings.Where(opening => opening.CompanyId == companyId && opening.DeletedAt == null),
                status,
                search)
            .CountAsync(ct);

    public Task<JobOpening?> FindByIdAsync(Guid companyId, Guid id, CancellationToken ct = default) =>
        db.JobOpenings
            .Include(opening => opening.Profiles)
            .ThenInclude(profile => profile.Requirements)
            .FirstOrDefaultAsync(
                opening => opening.Id == id && opening.CompanyId == companyId && opening.DeletedAt == null,
                ct);

    public Task<JobOpening?> FindForApplicationAsync(Guid id, CancellationToken ct = default) =>
        db.JobOpenings
            .AsNoTracking()
            .FirstOrDefaultAsync(opening => opening.Id == id && opening.DeletedAt == null, ct);

    public async Task<bool> IsProfileInUseAsync(Guid roleProfileId, CancellationToken ct = default)
    {
        // Evaluations are not mapped yet; a scalar query keeps this module independent.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "select exists(select 1 from app.evaluations where role_profile_id = @profile_id)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "profile_id";
            parameter.Value = roleProfileId;
            command.Parameters.Add(parameter);

            return await command.ExecuteScalarAsync(ct) is true;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public void Add(JobOpening jobOpening) => db.JobOpenings.Add(jobOpening);

    private static IQueryable<JobOpening> ApplyFilters(
        IQueryable<JobOpening> query,
        JobOpeningStatus? status,
        string? search)
    {
        if (status is { } statusFilter)
        {
            query = query.Where(opening => opening.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(opening =>
                EF.Functions.ILike(opening.Title, pattern)
                || (opening.Area != null && EF.Functions.ILike(opening.Area, pattern)));
        }

        return query;
    }
}
