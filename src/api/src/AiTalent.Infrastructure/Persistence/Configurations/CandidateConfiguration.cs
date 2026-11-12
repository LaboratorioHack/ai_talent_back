using AiTalent.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiTalent.Infrastructure.Persistence.Configurations;

internal sealed class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> builder)
    {
        builder.ToTable("candidates", "app");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.CompanyId).HasColumnName("company_id");
        builder.Property(c => c.UserId).HasColumnName("user_id");
        builder.Property(c => c.Name).HasColumnName("name");
        builder.Property(c => c.ContactEmail).HasColumnName("contact_email");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
    }
}
