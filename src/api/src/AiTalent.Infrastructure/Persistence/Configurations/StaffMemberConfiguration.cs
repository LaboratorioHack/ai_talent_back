using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiTalent.Infrastructure.Persistence.Configurations;

internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("interviewers", "app");
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(s => s.CompanyId).HasColumnName("company_id");
        builder.Property(s => s.Name).HasColumnName("name");
        builder.Property(s => s.Role)
            .HasColumnName("role")
            .HasConversion(
                role => role.ToString().ToUpperInvariant(),
                value => Enum.Parse<StaffRole>(value, ignoreCase: true));
        builder.Property(s => s.Active).HasColumnName("active");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
    }
}
