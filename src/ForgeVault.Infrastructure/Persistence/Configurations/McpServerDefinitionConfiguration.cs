using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class McpServerDefinitionConfiguration : IEntityTypeConfiguration<McpServerDefinition>
{
    public void Configure(EntityTypeBuilder<McpServerDefinition> builder)
    {
        builder.ToTable("mcp_server_definitions");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Transport).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();

        // Unique per Organization, not globally — two orgs can each have their own
        // "forgehub" definition.
        builder.HasIndex(m => new { m.OrganizationId, m.Name }).IsUnique();

        builder.HasOne(m => m.Organization)
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
