using ForgeVault.Api.Auditing;
using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Environment = ForgeVault.Domain.Entities.Environment;

namespace ForgeVault.Api.Endpoints;

// Project/Environment creation shared by REST (ProjectEndpoints/EnvironmentEndpoints) and MCP
// (admin.project.create/admin.environment.create), so both surfaces enforce the same checks:
// ProjectWrite at Organization scope for a project, EnvironmentWrite at the project for an
// environment. Errors are returned as the codes the callers already use.
internal static class TenancyProvisioning
{
    public static async Task<(ProjectResponse? Project, string? Error)> CreateProjectAsync(
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        Guid organizationId,
        string name,
        string slug,
        string? description,
        Guid actorId,
        AuditActorType actorType,
        string requestId,
        CancellationToken ct)
    {
        if (!await db.Organizations.AnyAsync(o => o.Id == organizationId, ct))
        {
            return (null, "organization_not_found");
        }

        if (!await permissions.HasPermissionAsync(actorId, Permission.ProjectWrite, new ResourceScope(organizationId, null, null), ct))
        {
            return (null, "forbidden");
        }

        if (await db.Projects.AnyAsync(p => p.OrganizationId == organizationId && p.Name == name, ct))
        {
            return (null, "project_name_taken");
        }

        if (await db.Projects.AnyAsync(p => p.OrganizationId == organizationId && p.Slug == slug, ct))
        {
            return (null, "project_slug_taken");
        }

        var now = DateTimeOffset.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Slug = slug,
            Description = description,
            Status = ProjectStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        db.AuditLogs.Add(AuditLogFactory.Create(actorId, actorType, "PROJECT_CREATE", "project", project.Id, requestId));

        await db.SaveChangesAsync(ct);
        return (ProjectEndpoints.ToResponse(project), null);
    }

    public static async Task<(EnvironmentResponse? Environment, string? Error)> CreateEnvironmentAsync(
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        Guid projectId,
        EnvironmentKind name,
        string slug,
        Guid actorId,
        AuditActorType actorType,
        string requestId,
        CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([projectId], ct);
        if (project is null)
        {
            return (null, "project_not_found");
        }

        if (!await permissions.HasPermissionAsync(actorId, Permission.EnvironmentWrite, new ResourceScope(project.OrganizationId, project.Id, null), ct))
        {
            return (null, "forbidden");
        }

        if (await db.Environments.AnyAsync(e => e.ProjectId == projectId && (e.Name == name || e.Slug == slug), ct))
        {
            return (null, "environment_already_exists");
        }

        var environment = new Environment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = name,
            Slug = slug,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Environments.Add(environment);
        db.AuditLogs.Add(AuditLogFactory.Create(actorId, actorType, "ENVIRONMENT_CREATE", "environment", environment.Id, requestId));
        await db.SaveChangesAsync(ct);

        return (EnvironmentEndpoints.ToResponse(environment), null);
    }
}
