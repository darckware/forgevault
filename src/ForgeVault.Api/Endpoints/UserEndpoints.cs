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

            var now = DateTimeOffset.UtcNow;
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                Email = request.Email,
                PasswordHash = passwordHasher.Hash(request.Password),
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
        user.Id, user.Email, user.MfaEnabled, user.IsActive, user.CreatedAt);
}

public sealed record CreateUserRequest(string Email, string Password);

public sealed record UserResponse(Guid Id, string Email, bool MfaEnabled, bool IsActive, DateTimeOffset CreatedAt);
