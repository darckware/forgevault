using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class ServiceAccountTokenConfiguration : IEntityTypeConfiguration<ServiceAccountToken>
{
    public void Configure(EntityTypeBuilder<ServiceAccountToken> builder)
    {
        builder.ToTable("service_account_tokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.TokenPrefix).IsRequired().HasMaxLength(40);

        builder.Property(t => t.IssuedAt).IsRequired();
    }
}
