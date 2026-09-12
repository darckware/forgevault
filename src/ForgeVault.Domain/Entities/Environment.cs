namespace ForgeVault.Domain.Entities;

// docs/modules/01_FOUNDATION_AND_TENANCY.md §4 — segregation by environment (§9/§140 of
// docs/ForgeVault.md: production credentials are never auto-shared with other environments).
public enum EnvironmentKind
{
    Development,
    Staging,
    Production,
    Shared,
}

public sealed class Environment
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public EnvironmentKind Name { get; set; }
    public required string Slug { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Project? Project { get; set; }
    public ICollection<Secret> Secrets { get; set; } = new List<Secret>();
}
