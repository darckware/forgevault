using ForgeVault.Domain.Entities;

namespace ForgeVault.Api.Auditing;

// docs/modules/06_AUDIT_AND_GOVERNANCE.md. Centralizes the AuditLog shape so every call
// site supplies the same required fields — never the secret value (docs/ForgeVault.md §21).
internal static class AuditLogFactory
{
    public static AuditLog Create(
        Guid actorId,
        AuditActorType actorType,
        string action,
        string resourceType,
        Guid resourceId,
        string requestId,
        string metadata = "{}") => new()
    {
        Id = Guid.NewGuid(),
        ActorId = actorId.ToString(),
        ActorType = actorType,
        Action = action,
        ResourceType = resourceType,
        ResourceId = resourceId,
        RequestId = requestId,
        Timestamp = DateTimeOffset.UtcNow,
        Metadata = metadata,
    };
}
