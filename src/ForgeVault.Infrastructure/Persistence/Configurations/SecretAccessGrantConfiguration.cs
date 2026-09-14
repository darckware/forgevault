using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class SecretAccessGrantConfiguration : IEntityTypeConfiguration<SecretAccessGrant>
{
    public void Configure(EntityTypeBuilder<SecretAccessGrant> builder)
    {
        builder.ToTable("secret_access_grants");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.CreatedAt).IsRequired();

        // Every lookup ("can this identity read this secret", "who has access to this
        // secret", idempotency on grant) filters by (secret, identity) — same shape as
        // McpServerAssignment's (identity, definition) index.
        builder.HasIndex(g => new { g.SecretId, g.IdentityId });

        builder.HasOne(g => g.Secret)
            .WithMany()
            .HasForeignKey(g => g.SecretId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
