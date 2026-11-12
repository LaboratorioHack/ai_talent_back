using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiTalent.Infrastructure.Persistence.Configurations;

internal sealed class JobOpeningConfiguration : IEntityTypeConfiguration<JobOpening>
{
    public void Configure(EntityTypeBuilder<JobOpening> builder)
    {
        builder.ToTable("job_openings", "app");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(o => o.CompanyId).HasColumnName("company_id");
        builder.Property(o => o.CreatorId).HasColumnName("creator_id");
        builder.Property(o => o.Title).HasColumnName("title");
        builder.Property(o => o.Area).HasColumnName("area");
        builder.Property(o => o.ClosingAt).HasColumnName("closing_at");
        builder.Property(o => o.Positions).HasColumnName("positions");
        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<JobOpeningStatus>(value, ignoreCase: true));
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        builder.Property(o => o.DeletedAt).HasColumnName("deleted_at");

        builder.HasMany(o => o.Profiles)
            .WithOne()
            .HasForeignKey(profile => profile.JobOpeningId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
