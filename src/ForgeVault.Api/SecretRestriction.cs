using System.Text.Json;
using ForgeVault.Api.Auditing;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;

namespace ForgeVault.Api;

internal static class SecretRestriction
{
    internal static async Task ChangeAsync(ForgeVaultDbContext db, Secret secret, bool restricted,
        Guid actorId, AuditActorType actorType, string requestId, CancellationToken ct)
    {
        if (secret.Restricted == restricted) return;
        var previous = secret.Restricted;
        secret.Restricted = restricted;
        secret.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(AuditLogFactory.Create(actorId, actorType, "SECRET_RESTRICTION_CHANGED", "secret", secret.Id,
            requestId, JsonSerializer.Serialize(new { previous, restricted })));
        await db.SaveChangesAsync(ct);
    }
}
