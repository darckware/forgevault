using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
// See docs/modules/01_FOUNDATION_AND_TENANCY.md — alias avoids CS0104 against System.Environment.
using Environment = ForgeVault.Domain.Entities.Environment;

namespace ForgeVault.Infrastructure.Persistence;

public sealed class ForgeVaultDbContext(DbContextOptions<ForgeVaultDbContext> options)
    : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Environment> Environments => Set<Environment>();
    public DbSet<Secret> Secrets => Set<Secret>();
    public DbSet<SecretVersion> SecretVersions => Set<SecretVersion>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<ServiceAccount> ServiceAccounts => Set<ServiceAccount>();
    public DbSet<ServiceAccountToken> ServiceAccountTokens => Set<ServiceAccountToken>();
    public DbSet<McpServerDefinition> McpServerDefinitions => Set<McpServerDefinition>();
    public DbSet<McpServerAssignment> McpServerAssignments => Set<McpServerAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ForgeVaultDbContext).Assembly);
    }
}
