using AiTalent.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiTalent.Infrastructure.Persistence.Configurations;

internal sealed class RoleProfileConfiguration : IEntityTypeConfiguration<RoleProfile>
{
    public void Configure(EntityTypeBuilder<RoleProfile> builder)
    {
        builder.ToTable("role_profiles", "app");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(profile => profile.JobOpeningId).HasColumnName("job_opening_id");
        builder.Property(profile => profile.Version).HasColumnName("version");
        builder.Property(profile => profile.Description).HasColumnName("description");
        builder.Property(profile => profile.EvaluationPolicy)
            .HasColumnName("evaluation_policy")
            .HasColumnType("jsonb");
        builder.Property(profile => profile.CreatedAt).HasColumnName("created_at");

        builder.HasMany(profile => profile.Requirements)
            .WithOne()
            .HasForeignKey(requirement => requirement.RoleProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
