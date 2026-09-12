using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
// "Environment" collides with System.Environment (brought in by ImplicitUsings) — an
// alias directive always wins over a plain using-namespace directive, so this resolves
// the ambiguity without renaming the domain entity itself.
using Environment = ForgeVault.Domain.Entities.Environment;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class EnvironmentConfiguration : IEntityTypeConfiguration<Environment>
{
    public void Configure(EntityTypeBuilder<Environment> builder)
    {
        builder.ToTable("environments");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.Slug).IsRequired().HasMaxLength(255);

        // unique per project (docs/modules/01_FOUNDATION_AND_TENANCY.md §4)
        builder.HasIndex(e => new { e.ProjectId, e.Name }).IsUnique();
        builder.HasIndex(e => new { e.ProjectId, e.Slug }).IsUnique();

        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasMany(e => e.Secrets)
            .WithOne(s => s.Environment)
            .HasForeignKey(s => s.EnvironmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
