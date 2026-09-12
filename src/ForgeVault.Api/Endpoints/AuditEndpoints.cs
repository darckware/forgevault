using System.Security.Claims;
using ForgeVault.Application.Authorization;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

// docs/modules/06_AUDIT_AND_GOVERNANCE.md §7 (queryAuditLog); M8 (docs/modules/09,
// admin.audit.search). AuditRead is checked "anywhere" (not scoped to one
// Organization/Project/Environment) — see IPermissionChecker.HasPermissionAnywhereAsync:
// audit review is a cross-cutting governance concern, not confined to one part of the
// hierarchy (docs/architecture/IMPLEMENTATION_READINESS.md, M8 decision).
public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/audit", async (
            string? actorId,
            string? resourceType,
            Guid? resourceId,
            string? action,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (!await permissions.HasPermissionAnywhereAsync(user.GetUserId(), Permission.AuditRead, ct))
            {
                return Results.Forbid();
            }

            var query = db.AuditLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(actorId))
            {
                query = query.Where(a => a.ActorId == actorId);
            }

            if (!string.IsNullOrWhiteSpace(resourceType))
            {
                query = query.Where(a => a.ResourceType == resourceType);
            }

            if (resourceId is not null)
            {
                query = query.Where(a => a.ResourceId == resourceId);
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(a => a.Action == action);
            }

            if (from is not null)
            {
                query = query.Where(a => a.Timestamp >= from);
            }

            if (to is not null)
            {
                query = query.Where(a => a.Timestamp <= to);
            }

            var effectivePage = page is > 0 ? page.Value : 1;
            var effectivePageSize = pageSize is > 0 and <= 200 ? pageSize.Value : 50;

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .Select(a => new AuditLogResponse(
                    a.Id, a.ActorId, a.ActorType.ToString(), a.Action, a.ResourceType, a.ResourceId,
                    a.RequestId, a.CorrelationId, a.Timestamp, a.Metadata))
                .ToListAsync(ct);

            // Metadata is passed through as-is — it is itself already constrained to never
            // contain a secret value (docs/ForgeVault.md §21; every AuditLogFactory.Create
            // call site in this codebase only ever writes non-sensitive metadata).
            return Results.Ok(new AuditLogPage(items, effectivePage, effectivePageSize, total));
        }).RequireAuthorization();
    }
}

public sealed record AuditLogResponse(
    Guid Id,
    string ActorId,
    string ActorType,
    string Action,
    string ResourceType,
    Guid ResourceId,
    string RequestId,
    string? CorrelationId,
    DateTimeOffset Timestamp,
    string Metadata);

public sealed record AuditLogPage(IReadOnlyList<AuditLogResponse> Items, int Page, int PageSize, int Total);
