using System.Security.Claims;
using ForgeVault.Api.Auditing;
using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

// docs/modules/01_FOUNDATION_AND_TENANCY.md. Thin CRUD; ProjectWrite RBAC added in M5
// (docs/architecture/IMPLEMENTATION_READINESS.md) — a RoleAssignment at Organization scope
// (or, once one exists, at the Project's own scope) is required to write.
public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/organizations/{organizationId:guid}/projects",
            async (
                Guid organizationId,
                CreateProjectRequest request,
                ForgeVaultDbContext db,
                IPermissionChecker permissions,
                ClaimsPrincipal user,
                HttpContext http,
                CancellationToken ct) =>
            {
                var organization = await db.Organizations.FindAsync([organizationId], ct);
                if (organization is null)
                {
                    return Results.NotFound(new ErrorResponse("organization_not_found"));
                }

                var identityId = user.GetUserId();
                var scope = new ResourceScope(organizationId, null, null);
                if (!await permissions.HasPermissionAsync(identityId, Permission.ProjectWrite, scope, ct))
                {
                    return Results.Forbid();
                }

                if (await db.Projects.AnyAsync(p => p.OrganizationId == organizationId && p.Name == request.Name, ct))
                {
                    return Results.Conflict(new ErrorResponse("project_name_taken"));
                }

                var now = DateTimeOffset.UtcNow;
                var project = new Project
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Name = request.Name,
                    Slug = request.Slug,
                    Description = request.Description,
                    Status = ProjectStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                db.Projects.Add(project);
                db.AuditLogs.Add(AuditLogFactory.Create(identityId, user.GetActorType(), "PROJECT_CREATE", "project", project.Id, http.TraceIdentifier));
                await db.SaveChangesAsync(ct);

                return Results.Created($"/api/v1/projects/{project.Id}", ToResponse(project));
            }).RequireAuthorization();

        app.MapGet("/api/v1/organizations/{organizationId:guid}/projects",
            async (Guid organizationId, ForgeVaultDbContext db, CancellationToken ct) =>
            {
                var projects = await db.Projects
                    .Where(p => p.OrganizationId == organizationId)
                    .OrderBy(p => p.Name)
                    .ToListAsync(ct);

                return Results.Ok(projects.Select(ToResponse));
            }).RequireAuthorization();

        var byId = app.MapGroup("/api/v1/projects").RequireAuthorization();

        byId.MapGet("/{id:guid}", async (Guid id, ForgeVaultDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects.FindAsync([id], ct);
            return project is null ? Results.NotFound() : Results.Ok(ToResponse(project));
        });

        byId.MapPut("/{id:guid}", async (
            Guid id,
            UpdateProjectRequest request,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var project = await db.Projects.FindAsync([id], ct);
            if (project is null)
            {
                return Results.NotFound();
            }

            var scope = new ResourceScope(project.OrganizationId, project.Id, null);
            if (!await permissions.HasPermissionAsync(user.GetUserId(), Permission.ProjectWrite, scope, ct))
            {
                return Results.Forbid();
            }

            if (request.Name is not null)
            {
                project.Name = request.Name;
            }

            if (request.Slug is not null)
            {
                project.Slug = request.Slug;
            }

            if (request.Description is not null)
            {
                project.Description = request.Description;
            }

            if (request.Status is not null)
            {
                project.Status = request.Status.Value;
            }

            project.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToResponse(project));
        });

        byId.MapDelete("/{id:guid}", async (
            Guid id,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var project = await db.Projects.FindAsync([id], ct);
            if (project is null)
            {
                return Results.NotFound();
            }

            var scope = new ResourceScope(project.OrganizationId, project.Id, null);
            if (!await permissions.HasPermissionAsync(user.GetUserId(), Permission.ProjectWrite, scope, ct))
            {
                return Results.Forbid();
            }

            if (await db.Environments.AnyAsync(e => e.ProjectId == id, ct))
            {
                return Results.Conflict(new ErrorResponse("project_has_environments"));
            }

            db.Projects.Remove(project);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    private static ProjectResponse ToResponse(Project project) => new(
        project.Id,
        project.OrganizationId,
        project.Name,
        project.Slug,
        project.Description,
        project.Status.ToString(),
        project.CreatedAt,
        project.UpdatedAt);
}

public sealed record CreateProjectRequest(string Name, string Slug, string? Description);

public sealed record UpdateProjectRequest(string? Name, string? Slug, string? Description, ProjectStatus? Status);

public sealed record ProjectResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Slug,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
