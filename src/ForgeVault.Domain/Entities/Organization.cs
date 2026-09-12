namespace ForgeVault.Domain.Entities;

// docs/modules/01_FOUNDATION_AND_TENANCY.md §4
public enum OrganizationStatus
{
    Active,
    Suspended,
}

public sealed class Organization
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
