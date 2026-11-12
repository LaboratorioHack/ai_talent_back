using AiTalent.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ApplicationEntity = AiTalent.Domain.Entities.Application;

namespace AiTalent.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<ApplicationEntity>
{
    public void Configure(EntityTypeBuilder<ApplicationEntity> builder)
    {
        builder.ToTable("applications", "app");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(a => a.JobOpeningId).HasColumnName("job_opening_id");
        builder.Property(a => a.CandidateId).HasColumnName("candidate_id");
        builder.Property(a => a.Source)
            .HasColumnName("source")
            .HasConversion(
                source => source.ToString().ToUpperInvariant(),
                value => Enum.Parse<ApplicationSource>(value, ignoreCase: true));
        builder.Property(a => a.SelectionStatus)
            .HasColumnName("selection_status")
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<ApplicationSelectionStatus>(value, ignoreCase: true));
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
    }
}
