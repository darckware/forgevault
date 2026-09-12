namespace ForgeVault.Domain.Entities;

// docs/modules/01_FOUNDATION_AND_TENANCY.md §4
public enum ProjectStatus
{
    Active,
    Suspended,
}

public sealed class Project
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Organization? Organization { get; set; }
    public ICollection<Environment> Environments { get; set; } = new List<Environment>();
}
