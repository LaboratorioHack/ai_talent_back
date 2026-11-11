using AiTalent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence;

/// <summary>
/// El esquema se gestiona con Supabase CLI (supabase/migrations). EF Core solo mapea; no genera migraciones.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
