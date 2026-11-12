using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiTalent.Infrastructure.Persistence.Configurations;

internal sealed class ResumeConfiguration : IEntityTypeConfiguration<Resume>
{
    public void Configure(EntityTypeBuilder<Resume> builder)
    {
        builder.ToTable("resumes", "app");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.CandidateId).HasColumnName("candidate_id");
        builder.Property(r => r.Version).HasColumnName("version");
        builder.Property(r => r.FileKey).HasColumnName("file_key");
        builder.Property(r => r.FileHash).HasColumnName("file_hash");
        builder.Property(r => r.ProcessingStatus)
            .HasColumnName("processing_status")
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<ResumeProcessingStatus>(value, ignoreCase: true));
        builder.Property(r => r.ProcessingError).HasColumnName("processing_error");
        builder.Property(r => r.ProfessionalData).HasColumnName("professional_data").HasColumnType("jsonb");
        builder.Property(r => r.ExtractorVersion).HasColumnName("extractor_version");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
    }
}
