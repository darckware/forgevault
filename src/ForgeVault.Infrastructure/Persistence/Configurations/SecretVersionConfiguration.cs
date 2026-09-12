using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class SecretVersionConfiguration : IEntityTypeConfiguration<SecretVersion>
{
    public void Configure(EntityTypeBuilder<SecretVersion> builder)
    {
        builder.ToTable("secret_versions");

        builder.HasKey(v => v.Id);

        // append-only: never updated after insert (docs/ForgeVault.md §22/§106).
        builder.HasIndex(v => new { v.SecretId, v.Version }).IsUnique();

        builder.Property(v => v.Ciphertext).IsRequired();
        builder.Property(v => v.EncryptedDek).IsRequired();
        builder.Property(v => v.Nonce).IsRequired();
        builder.Property(v => v.AuthTag).IsRequired();
        builder.Property(v => v.Algorithm).IsRequired().HasMaxLength(50).HasDefaultValue("AES-256-GCM");

        builder.Property(v => v.CreatedAt).IsRequired();
    }
}
