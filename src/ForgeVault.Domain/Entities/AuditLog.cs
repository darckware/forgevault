namespace ForgeVault.Domain.Entities;

// docs/modules/06_AUDIT_AND_GOVERNANCE.md §4.
public enum AuditActorType
{
    Human,
    Agent,
    Service,
    Machine,
    McpClient,
}

public sealed class AuditLog
{
    public Guid Id { get; set; }

    public required string ActorId { get; set; }
    public AuditActorType ActorType { get; set; }

    // Open-ended (grows across modules over time — LOGIN, SECRET_READ, SECRET_REVEAL,
    // ACCESS_GRANTED, ...), deliberately a plain string rather than a fixed enum/CHECK,
    // per docs/modules/06_AUDIT_AND_GOVERNANCE.md §4.
    public required string Action { get; set; }

    // Polymorphic reference to any entity in the system — no real FK, same convention
    // used by ForgeHub for Approval/AuditEvent (docs/modules/06_AUDIT_AND_GOVERNANCE.md §4).
    public required string ResourceType { get; set; }
    public Guid ResourceId { get; set; }

    public string? SourceIp { get; set; }
    public string? UserAgent { get; set; }
    public required string RequestId { get; set; }
    public string? CorrelationId { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    // NEVER contains a secret value, password, or raw token (docs/ForgeVault.md §21;
    // docs/modules/06_AUDIT_AND_GOVERNANCE.md §4 invariant 1) — enforced by review and by
    // the security test suite, not by a DB constraint (Postgres cannot validate JSONB content
    // against an open-ended denylist).
    public string Metadata { get; set; } = "{}";
}
