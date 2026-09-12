using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActorId).IsRequired().HasMaxLength(255);
        builder.Property(a => a.ActorType).HasConversion<string>().HasMaxLength(32).IsRequired();

        // Open-ended, plain string — see docs/modules/06_AUDIT_AND_GOVERNANCE.md §4.
        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);

        // Polymorphic (resource_type, resource_id) — no real FK, same convention as ForgeHub.
        builder.Property(a => a.ResourceType).IsRequired().HasMaxLength(100);

        builder.Property(a => a.RequestId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.CorrelationId).HasMaxLength(100);
        builder.Property(a => a.SourceIp).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(512);

        builder.Property(a => a.Timestamp).IsRequired();

        builder.Property(a => a.Metadata).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(a => new { a.ActorId, a.Timestamp });
        builder.HasIndex(a => new { a.ResourceType, a.ResourceId });

        // Append-only: no delete/update path is exposed by design
        // (docs/modules/06_AUDIT_AND_GOVERNANCE.md §4 invariant 2).
    }
}
