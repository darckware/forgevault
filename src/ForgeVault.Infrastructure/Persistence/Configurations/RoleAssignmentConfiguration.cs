using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable("role_assignments");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.ScopeType).HasConversion<string>().HasMaxLength(32).IsRequired();

        // Every permission check filters by (identity, scope) — index accordingly.
        builder.HasIndex(r => new { r.IdentityId, r.ScopeType, r.ScopeId });

        builder.Property(r => r.CreatedAt).IsRequired();
    }
}
