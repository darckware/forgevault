using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(255);
        builder.Property(p => p.Slug).IsRequired().HasMaxLength(255);
        builder.Property(p => p.Description).HasMaxLength(2000);

        // unique per organization (docs/modules/01_FOUNDATION_AND_TENANCY.md §4)
        builder.HasIndex(p => new { p.OrganizationId, p.Name }).IsUnique();
        builder.HasIndex(p => new { p.OrganizationId, p.Slug }).IsUnique();

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        builder.HasMany(p => p.Environments)
            .WithOne(e => e.Project)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
