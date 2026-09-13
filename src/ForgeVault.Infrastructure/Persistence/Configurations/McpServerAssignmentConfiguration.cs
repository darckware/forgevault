using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class McpServerAssignmentConfiguration : IEntityTypeConfiguration<McpServerAssignment>
{
    public void Configure(EntityTypeBuilder<McpServerAssignment> builder)
    {
        builder.ToTable("mcp_server_assignments");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ParamValuesJson).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();

        // Every lookup ("what is this identity assigned to") and idempotency check filters
        // by (identity, definition) — same shape as RoleAssignment's index.
        builder.HasIndex(m => new { m.IdentityId, m.McpServerDefinitionId });

        builder.HasOne(m => m.McpServerDefinition)
            .WithMany()
            .HasForeignKey(m => m.McpServerDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
