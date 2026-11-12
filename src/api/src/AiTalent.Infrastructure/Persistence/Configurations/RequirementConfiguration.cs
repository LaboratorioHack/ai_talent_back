using System.Text.Json;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiTalent.Infrastructure.Persistence.Configurations;

internal sealed class RequirementConfiguration : IEntityTypeConfiguration<Requirement>
{
    public void Configure(EntityTypeBuilder<Requirement> builder)
    {
        builder.ToTable("requirements", "app");
        builder.HasKey(requirement => requirement.Id);
        builder.Property(requirement => requirement.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(requirement => requirement.RoleProfileId).HasColumnName("role_profile_id");
        builder.Property(requirement => requirement.Type)
            .HasColumnName("type")
            .HasConversion(
                type => type.ToString().ToUpperInvariant(),
                value => Enum.Parse<RequirementType>(value, ignoreCase: true));
        builder.Property(requirement => requirement.Category).HasColumnName("category");
        builder.Property(requirement => requirement.Description).HasColumnName("description");
        builder.Property(requirement => requirement.Condition).HasColumnName("condition");
        builder.Property(requirement => requirement.Weight).HasColumnName("weight").HasColumnType("numeric");
        builder.Property(requirement => requirement.Equivalences)
            .HasColumnName("equivalences")
            .HasColumnType("jsonb")
            .HasConversion(
                equivalences => JsonSerializer.Serialize(equivalences, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<Dictionary<string, int>>(json, (JsonSerializerOptions?)null)
                    ?? new Dictionary<string, int>());
    }
}
