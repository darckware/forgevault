namespace ForgeVault.Domain.Entities;

// docs/ForgeVault.md §76, §115-117: "fv_sa_..." token, stored only as a hash — the raw
// value is shown exactly once, at issuance (docs/ForgeVault.md §115).
public sealed class ServiceAccountToken
{
    public Guid Id { get; set; }
    public Guid ServiceAccountId { get; set; }

    public required string TokenHash { get; set; }

    // e.g. "fv_sa_forgehub_****8f2a" (docs/ForgeVault.md §117) — safe to display in a list
    // without revealing the token itself.
    public required string TokenPrefix { get; set; }

    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public ServiceAccount? ServiceAccount { get; set; }
}
