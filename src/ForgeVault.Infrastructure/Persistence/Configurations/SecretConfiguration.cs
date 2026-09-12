using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class SecretConfiguration : IEntityTypeConfiguration<Secret>
{
    public void Configure(EntityTypeBuilder<Secret> builder)
    {
        builder.ToTable("secrets");

        builder.HasKey(s => s.Id);

        // convention PROVIDER_RESOURCE_PURPOSE, docs/ForgeVault.md §64 — not enforced at
        // the DB layer (naming convention, not a hard constraint).
        builder.Property(s => s.Name).IsRequired().HasMaxLength(255);
        builder.HasIndex(s => new { s.EnvironmentId, s.Name }).IsUnique();

        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(s => s.Provider).HasMaxLength(100);
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        builder.HasMany(s => s.Versions)
            .WithOne(v => v.Secret)
            .HasForeignKey(v => v.SecretId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
