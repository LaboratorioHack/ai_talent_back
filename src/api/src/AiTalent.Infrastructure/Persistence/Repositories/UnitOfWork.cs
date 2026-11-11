using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    private const string UniqueViolation = "23505";

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            throw new DuplicateEntityException("A record with the same key already exists.", ex);
        }
    }
}
