using System.Security.Claims;
using ForgeVault.Api.Auditing;
using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
// See docs/modules/01_FOUNDATION_AND_TENANCY.md — alias avoids CS0104 against System.Environment.
using Environment = ForgeVault.Domain.Entities.Environment;

namespace ForgeVault.Api.Endpoints;

// docs/modules/01_FOUNDATION_AND_TENANCY.md. Thin CRUD — no PUT: the only mutable-in-practice
// field (Name) is the enum that defines the row's identity within a project, so there is
// nothing meaningful to update short of delete+recreate. EnvironmentWrite RBAC added in M5
// (docs/architecture/IMPLEMENTATION_READINESS.md).
public static class EnvironmentEndpoints
{
    public static void MapEnvironmentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/projects/{projectId:guid}/environments",
            async (
                Guid projectId,
                CreateEnvironmentRequest request,
                ForgeVaultDbContext db,
                IPermissionChecker permissions,
                ClaimsPrincipal user,
                HttpContext http,
                CancellationToken ct) =>
            {
                var (environment, error) = await TenancyProvisioning.CreateEnvironmentAsync(
                    db, permissions, projectId, request.Name, request.Slug,
                    user.GetUserId(), user.GetActorType(), http.TraceIdentifier, ct);
                return error switch
                {
                    null => Results.Created($"/api/v1/environments/{environment!.Id}", environment),
                    "project_not_found" => Results.NotFound(new ErrorResponse(error)),
                    "forbidden" => Results.Forbid(),
                    _ => Results.Conflict(new ErrorResponse(error)),
                };
            }).RequireAuthorization();

        app.MapGet("/api/v1/projects/{projectId:guid}/environments",
            async (Guid projectId, ForgeVaultDbContext db, CancellationToken ct) =>
            {
                var environments = await db.Environments
                    .Where(e => e.ProjectId == projectId)
                    .OrderBy(e => e.Name)
                    .ToListAsync(ct);

                return Results.Ok(environments.Select(ToResponse));
            }).RequireAuthorization();

        var byId = app.MapGroup("/api/v1/environments").RequireAuthorization();

        byId.MapGet("/{id:guid}", async (Guid id, ForgeVaultDbContext db, CancellationToken ct) =>
        {
            var environment = await db.Environments.FindAsync([id], ct);
            return environment is null ? Results.NotFound() : Results.Ok(ToResponse(environment));
        });

        byId.MapDelete("/{id:guid}", async (
            Guid id,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var environment = await db.Environments
                .Where(e => e.Id == id)
                .Select(e => new { Entity = e, e.ProjectId, OrganizationId = e.Project!.OrganizationId })
                .SingleOrDefaultAsync(ct);

            if (environment is null)
            {
                return Results.NotFound();
            }

            var scope = new ResourceScope(environment.OrganizationId, environment.ProjectId, id);
            if (!await permissions.HasPermissionAsync(user.GetUserId(), Permission.EnvironmentWrite, scope, ct))
            {
                return Results.Forbid();
            }

            if (await db.Secrets.AnyAsync(s => s.EnvironmentId == id, ct))
            {
                return Results.Conflict(new ErrorResponse("environment_has_secrets"));
            }

            db.Environments.Remove(environment.Entity);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    internal static EnvironmentResponse ToResponse(Environment environment) => new(
        environment.Id,
        environment.ProjectId,
        environment.Name.ToString(),
        environment.Slug,
        environment.CreatedAt);
}

public sealed record CreateEnvironmentRequest(EnvironmentKind Name, string Slug);

public sealed record EnvironmentResponse(Guid Id, Guid ProjectId, string Name, string Slug, DateTimeOffset CreatedAt);
