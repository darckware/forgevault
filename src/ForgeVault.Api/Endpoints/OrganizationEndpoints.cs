using System.Security.Claims;
using ForgeVault.Api.Auditing;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

// docs/modules/01_FOUNDATION_AND_TENANCY.md. Thin CRUD for milestone M4/M5
// (docs/architecture/IMPLEMENTATION_READINESS.md). Organization creation deliberately has
// no RBAC scoping beyond authentication: it is the root of the hierarchy, so there is no
// parent scope to inherit a role from — gating it properly belongs to module 01's actual
// bootstrap flow (docs/ForgeVault.md §141), not yet implemented as code. Every other
// resource (Project/Environment/Secret) inherits RBAC from the Organization/Project scope
// once a role is assigned there.
public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations").RequireAuthorization();

        group.MapPost("/", async (CreateOrganizationRequest request, ForgeVaultDbContext db, ClaimsPrincipal user, HttpContext http, CancellationToken ct) =>
        {
            if (await db.Organizations.AnyAsync(o => o.Name == request.Name, ct))
            {
                return Results.Conflict(new ErrorResponse("organization_name_taken"));
            }

            var now = DateTimeOffset.UtcNow;
            var organization = new Organization
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Slug = request.Slug,
                Status = OrganizationStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Organizations.Add(organization);
            db.AuditLogs.Add(AuditLogFactory.Create(
                user.GetUserId(), user.GetActorType(), "ORGANIZATION_CREATE", "organization", organization.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/organizations/{organization.Id}", ToResponse(organization));
        });

        group.MapGet("/", async (ForgeVaultDbContext db, CancellationToken ct) =>
        {
            var organizations = await db.Organizations.OrderBy(o => o.Name).ToListAsync(ct);
            return Results.Ok(organizations.Select(ToResponse));
        });

        group.MapGet("/{id:guid}", async (Guid id, ForgeVaultDbContext db, CancellationToken ct) =>
        {
            var organization = await db.Organizations.FindAsync([id], ct);
            return organization is null ? Results.NotFound() : Results.Ok(ToResponse(organization));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateOrganizationRequest request, ForgeVaultDbContext db, CancellationToken ct) =>
        {
            var organization = await db.Organizations.FindAsync([id], ct);
            if (organization is null)
            {
                return Results.NotFound();
            }

            if (request.Name is not null)
            {
                organization.Name = request.Name;
            }

            if (request.Slug is not null)
            {
                organization.Slug = request.Slug;
            }

            if (request.Status is not null)
            {
                organization.Status = request.Status.Value;
            }

            organization.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToResponse(organization));
        });

        group.MapDelete("/{id:guid}", async (Guid id, ForgeVaultDbContext db, CancellationToken ct) =>
        {
            var organization = await db.Organizations.FindAsync([id], ct);
            if (organization is null)
            {
                return Results.NotFound();
            }

            // docs/modules/01_FOUNDATION_AND_TENANCY.md §4 invariant 1.
            if (await db.Projects.AnyAsync(p => p.OrganizationId == id, ct))
            {
                return Results.Conflict(new ErrorResponse("organization_has_projects"));
            }

            db.Organizations.Remove(organization);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    private static OrganizationResponse ToResponse(Organization organization) => new(
        organization.Id,
        organization.Name,
        organization.Slug,
        organization.Status.ToString(),
        organization.CreatedAt,
        organization.UpdatedAt);
}

public sealed record CreateOrganizationRequest(string Name, string Slug);

public sealed record UpdateOrganizationRequest(string? Name, string? Slug, OrganizationStatus? Status);

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
