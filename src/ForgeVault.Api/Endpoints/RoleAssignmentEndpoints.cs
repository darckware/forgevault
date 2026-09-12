using System.Security.Claims;
using ForgeVault.Api.Auditing;
using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

// docs/modules/04_AUTHORIZATION_AND_POLICY.md §7 (assignRole/revokeRoleAssignment) + UC-03
// (listing). M9: this closes the real bottleneck left open since M5 — granting any
// RoleAssignment required a direct INSERT against the database, with no endpoint at all
// (documented gap, e.g. in RbacRevealAuditTests' setup and INTEGRATION_CONTRACT_MVP.md).
// Authorization mirrors the module spec literally ("role in [OWNER, ADMIN]"): only an
// identity holding RoleAssignmentWrite (Owner/Admin — see RolePermissions.cs) at the scope
// being granted into may grant or revoke a role there.
public static class RoleAssignmentEndpoints
{
    public static void MapRoleAssignmentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/identities/{identityId:guid}/role-assignments", async (
            Guid identityId,
            AssignRoleRequest request,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var scope = await RoleAssignmentScopeResolver.ResolveAsync(db, request.ScopeType, request.ScopeId, ct);
            if (scope is null)
            {
                return Results.NotFound(new ErrorResponse("scope_not_found"));
            }

            var callerId = user.GetUserId();
            if (!await permissions.HasPermissionAsync(callerId, Permission.RoleAssignmentWrite, scope, ct))
            {
                return Results.Forbid();
            }

            // Idempotency by (identity_id, role, scope_type, scope_id) — module 04 §7:
            // granting the same role at the same scope twice returns the existing grant
            // instead of creating a duplicate active row.
            var existing = await db.RoleAssignments.SingleOrDefaultAsync(r =>
                r.IdentityId == identityId && r.Role == request.Role &&
                r.ScopeType == request.ScopeType && r.ScopeId == request.ScopeId &&
                r.RevokedAt == null, ct);
            if (existing is not null)
            {
                return Results.Ok(ToResponse(existing));
            }

            var assignment = new RoleAssignment
            {
                Id = Guid.NewGuid(),
                IdentityId = identityId,
                Role = request.Role,
                ScopeType = request.ScopeType,
                ScopeId = request.ScopeId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.RoleAssignments.Add(assignment);
            db.AuditLogs.Add(AuditLogFactory.Create(
                callerId, user.GetActorType(), "ACCESS_GRANTED", "role_assignment", assignment.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/role-assignments/{assignment.Id}", ToResponse(assignment));
        }).RequireAuthorization();

        app.MapPost("/api/v1/role-assignments/{id:guid}/revoke", async (
            Guid id,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var assignment = await db.RoleAssignments.FindAsync([id], ct);
            if (assignment is null)
            {
                return Results.NotFound();
            }

            var scope = await RoleAssignmentScopeResolver.ResolveAsync(db, assignment.ScopeType, assignment.ScopeId, ct);
            var callerId = user.GetUserId();
            if (scope is null || !await permissions.HasPermissionAsync(callerId, Permission.RoleAssignmentWrite, scope, ct))
            {
                return Results.Forbid();
            }

            if (assignment.RevokedAt is null)
            {
                assignment.RevokedAt = DateTimeOffset.UtcNow;
                db.AuditLogs.Add(AuditLogFactory.Create(
                    callerId, user.GetActorType(), "ACCESS_REVOKED", "role_assignment", assignment.Id, http.TraceIdentifier));
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(ToResponse(assignment));
        }).RequireAuthorization();

        // UC-03: "Lista roles atribuídos a uma identidade" — gated the same way as
        // admin.audit.search (RoleAssignmentWrite anywhere), since it's a governance view
        // over someone else's access, not a self-service "show my own roles" endpoint.
        app.MapGet("/api/v1/identities/{identityId:guid}/role-assignments", async (
            Guid identityId,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (!await permissions.HasPermissionAnywhereAsync(user.GetUserId(), Permission.RoleAssignmentWrite, ct))
            {
                return Results.Forbid();
            }

            var assignments = await db.RoleAssignments
                .Where(r => r.IdentityId == identityId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct);

            return Results.Ok(assignments.Select(ToResponse));
        }).RequireAuthorization();
    }

    private static RoleAssignmentResponse ToResponse(RoleAssignment assignment) => new(
        assignment.Id,
        assignment.IdentityId,
        assignment.Role.ToString(),
        assignment.ScopeType.ToString(),
        assignment.ScopeId,
        assignment.RevokedAt is null ? "active" : "revoked",
        assignment.CreatedAt,
        assignment.RevokedAt);
}

public sealed record AssignRoleRequest(Role Role, RoleScopeType ScopeType, Guid ScopeId);

public sealed record RoleAssignmentResponse(
    Guid Id,
    Guid IdentityId,
    string Role,
    string ScopeType,
    Guid ScopeId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);
