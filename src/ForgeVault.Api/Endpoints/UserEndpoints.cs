using System.Security.Claims;
using ForgeVault.Api.Auditing;
using ForgeVault.Application.Auth;
using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

// M13 — closes a gap present since the very first commit: there was no way to create a
// human User account except a direct INSERT against the database (every test file's
// CreateAuthenticatedClientAsync helper does exactly that). Same shape as the gap
// RoleAssignmentEndpoints (M9) closed for role grants: an Owner/Admin can now do this
// through the app itself, gated by the new UserManage permission.
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/users").RequireAuthorization();

        group.MapPost("/", async (
            CreateUserRequest request,
            ForgeVaultDbContext db,
            IPasswordHasher passwordHasher,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId();
            if (!await permissions.HasPermissionAnywhereAsync(callerId, Permission.UserManage, ct))
            {
                return Results.Forbid();
            }

            if (request.Password.Length < 8)
            {
                return Results.Json(new ErrorResponse("password_too_short"), statusCode: StatusCodes.Status400BadRequest);
            }

            if (await db.Users.AnyAsync(u => u.Email == request.Email, ct))
            {
                return Results.Conflict(new ErrorResponse("email_taken"));
            }

            if (request.Username is { Length: > 0 } && await db.Users.AnyAsync(u => u.Username == request.Username, ct))
            {
                return Results.Conflict(new ErrorResponse("username_taken"));
            }

            var now = DateTimeOffset.UtcNow;
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                Email = request.Email,
                PasswordHash = passwordHasher.Hash(request.Password),
                Username = request.Username is { Length: > 0 } ? request.Username : null,
                FirstName = request.FirstName is { Length: > 0 } ? request.FirstName : null,
                LastName = request.LastName is { Length: > 0 } ? request.LastName : null,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Users.Add(newUser);
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "USER_CREATED", "user", newUser.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/users/{newUser.Id}", ToResponse(newUser));
        });

        group.MapGet("/", async (
            ForgeVaultDbContext db, IPermissionChecker permissions, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!await permissions.HasPermissionAnywhereAsync(user.GetUserId(), Permission.UserManage, ct))
            {
                return Results.Forbid();
            }

            var users = await db.Users.OrderBy(u => u.Email).ToListAsync(ct);
            return Results.Ok(users.Select(ToResponse));
        });

        group.MapPost("/{id:guid}/deactivate", async (
            Guid id, ForgeVaultDbContext db, IPermissionChecker permissions, ClaimsPrincipal user, HttpContext http, CancellationToken ct) =>
            await SetActiveAsync(id, isActive: false, "USER_DEACTIVATED", db, permissions, user, http, ct));

        group.MapPost("/{id:guid}/reactivate", async (
            Guid id, ForgeVaultDbContext db, IPermissionChecker permissions, ClaimsPrincipal user, HttpContext http, CancellationToken ct) =>
            await SetActiveAsync(id, isActive: true, "USER_REACTIVATED", db, permissions, user, http, ct));

        // Admin-side profile edit — Username/IsAdmin specifically stay out of
        // PUT /api/v1/auth/me (self-service) on purpose, same boundary ForgeHub draws
        // between SelfUserUpdate and its admin-only /users/{id} route. IsAdmin here is the
        // cosmetic badge only (see User.IsAdmin) — never a Permission bypass.
        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateUserRequest request,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId();
            if (!await permissions.HasPermissionAnywhereAsync(callerId, Permission.UserManage, ct))
            {
                return Results.Forbid();
            }

            var target = await db.Users.FindAsync([id], ct);
            if (target is null)
            {
                return Results.NotFound();
            }

            if (request.Username is not null)
            {
                var normalized = request.Username.Length == 0 ? null : request.Username;
                if (normalized is not null && await db.Users.AnyAsync(u => u.Id != id && u.Username == normalized, ct))
                {
                    return Results.Conflict(new ErrorResponse("username_taken"));
                }

                target.Username = normalized;
            }

            if (request.FirstName is not null)
            {
                target.FirstName = request.FirstName.Length == 0 ? null : request.FirstName;
            }

            if (request.LastName is not null)
            {
                target.LastName = request.LastName.Length == 0 ? null : request.LastName;
            }

            if (request.IsAdmin is { } isAdmin && target.IsAdmin != isAdmin)
            {
                target.IsAdmin = isAdmin;
                db.AuditLogs.Add(AuditLogFactory.Create(
                    callerId, user.GetActorType(), isAdmin ? "USER_ADMIN_BADGE_GRANTED" : "USER_ADMIN_BADGE_REVOKED", "user", target.Id, http.TraceIdentifier));
            }

            target.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToResponse(target));
        });

        // Hard delete — the CRUD verb deactivate/reactivate never covered (those keep the
        // row, audit trail and RoleAssignments intact; this actually removes the identity).
        // Meant for cleaning up an account that should never have existed (a mistaken
        // invite, leftover test data), not routine offboarding — deactivate is the
        // reversible, audit-preserving choice for that.
        group.MapDelete("/{id:guid}", async (
            Guid id, ForgeVaultDbContext db, IPermissionChecker permissions, ClaimsPrincipal user, HttpContext http, CancellationToken ct) =>
        {
            var callerId = user.GetUserId();
            if (!await permissions.HasPermissionAnywhereAsync(callerId, Permission.UserManage, ct))
            {
                return Results.Forbid();
            }

            var target = await db.Users.FindAsync([id], ct);
            if (target is null)
            {
                return Results.NotFound();
            }

            if (id == callerId)
            {
                // Same "don't let an actor strand itself" principle as deactivate — deleting
                // your own account here would leave a caller mid-request with no way back.
                return Results.Json(new ErrorResponse("cannot_delete_self"), statusCode: StatusCodes.Status409Conflict);
            }

            // RoleAssignment.IdentityId is a soft reference (module 04 — no FK to any
            // identity table, since an identity can be a User or a ServiceAccount). Deleting
            // the User row wouldn't fail without this, but would leave live-looking
            // RoleAssignment rows pointing at an identity that no longer exists — revoke them
            // explicitly first, same audited action a real RoleAssignmentEndpoints revoke
            // would produce, not silent orphaning.
            var activeAssignments = await db.RoleAssignments
                .Where(r => r.IdentityId == id && r.RevokedAt == null)
                .ToListAsync(ct);
            var now = DateTimeOffset.UtcNow;
            foreach (var assignment in activeAssignments)
            {
                assignment.RevokedAt = now;
                db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "ACCESS_REVOKED", "role_assignment", assignment.Id, http.TraceIdentifier));
            }

            db.Users.Remove(target);
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "USER_DELETED", "user", target.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    private static async Task<IResult> SetActiveAsync(
        Guid id, bool isActive, string auditAction,
        ForgeVaultDbContext db, IPermissionChecker permissions, ClaimsPrincipal user, HttpContext http, CancellationToken ct)
    {
        var callerId = user.GetUserId();
        if (!await permissions.HasPermissionAnywhereAsync(callerId, Permission.UserManage, ct))
        {
            return Results.Forbid();
        }

        var target = await db.Users.FindAsync([id], ct);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (id == callerId && !isActive)
        {
            // Deactivating your own account through this endpoint would leave a caller
            // locked out with no other Owner/Admin necessarily available — same
            // "don't let an actor strand itself" principle already applied elsewhere
            // (e.g. an agent can't grant itself Owner in admin.role.grant).
            return Results.Json(new ErrorResponse("cannot_deactivate_self"), statusCode: StatusCodes.Status409Conflict);
        }

        if (target.IsActive != isActive)
        {
            target.IsActive = isActive;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), auditAction, "user", target.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(ToResponse(target));
    }

    private static UserResponse ToResponse(User user) => new(
        user.Id, user.Email, user.MfaEnabled, user.IsActive,
        user.Username, user.FirstName, user.LastName, user.AvatarDataUrl, user.IsAdmin,
        user.CreatedAt);
}

public sealed record CreateUserRequest(string Email, string Password, string? Username = null, string? FirstName = null, string? LastName = null);

public sealed record UpdateUserRequest(string? Username, string? FirstName, string? LastName, bool? IsAdmin);

public sealed record UserResponse(
    Guid Id, string Email, bool MfaEnabled, bool IsActive,
    string? Username, string? FirstName, string? LastName, string? AvatarDataUrl, bool IsAdmin,
    DateTimeOffset CreatedAt);
