using System.Security.Claims;
using System.Text.Json;
using ForgeVault.Api.Auditing;
using ForgeVault.Application.Authorization;
using ForgeVault.Application.Security;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

// docs/modules/11_MCP_REGISTRY.md (M10). ForgeVault as the single source of truth for which
// MCP servers exist and which identity is wired to which — closing the gap where every
// Hermes agent's mcp_servers: block (including plaintext tokens like FORGEHUB_AGENT_TOKEN)
// was hand-edited into config.yaml. This module never writes to a host's config.yaml itself
// (deploy/scripts/sync_mcp_config.py does that, as an explicit, reviewable step) — it only
// owns the catalog, the per-identity assignment, and rendering the resolved config on demand.
public static class McpRegistryEndpoints
{
    public static void MapMcpRegistryEndpoints(this WebApplication app)
    {
        // --- Catalog ---

        app.MapPost("/api/v1/organizations/{organizationId:guid}/mcp-servers", async (
            Guid organizationId,
            CreateMcpServerDefinitionRequest request,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!await db.Organizations.AnyAsync(o => o.Id == organizationId, ct))
            {
                return Results.NotFound(new ErrorResponse("organization_not_found"));
            }

            var callerId = user.GetUserId();
            var scope = new ResourceScope(organizationId, null, null);
            if (!await permissions.HasPermissionAsync(callerId, Permission.McpRegistryWrite, scope, ct))
            {
                return Results.Forbid();
            }

            if (await db.McpServerDefinitions.AnyAsync(m => m.OrganizationId == organizationId && m.Name == request.Name, ct))
            {
                return Results.Conflict(new ErrorResponse("mcp_server_name_taken"));
            }

            var definition = new McpServerDefinition
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = request.Name,
                Transport = request.Transport,
                Command = request.Command,
                ArgsJson = request.Args is null ? null : JsonSerializer.Serialize(request.Args),
                Url = request.Url,
                Timeout = request.Timeout,
                ConnectTimeout = request.ConnectTimeout,
                StaticEnvJson = request.StaticEnv is null ? null : JsonSerializer.Serialize(request.StaticEnv),
                SecretParamNamesJson = request.SecretParamNames is null ? null : JsonSerializer.Serialize(request.SecretParamNames),
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.McpServerDefinitions.Add(definition);
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "MCP_SERVER_REGISTERED", "mcp_server_definition", definition.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/mcp-servers/{definition.Id}", ToResponse(definition));
        }).RequireAuthorization();

        app.MapGet("/api/v1/organizations/{organizationId:guid}/mcp-servers", async (
            Guid organizationId, ForgeVaultDbContext db, CancellationToken ct) =>
        {
            var definitions = await db.McpServerDefinitions
                .Where(m => m.OrganizationId == organizationId)
                .OrderBy(m => m.Name)
                .ToListAsync(ct);
            return Results.Ok(definitions.Select(ToResponse));
        }).RequireAuthorization();

        app.MapGet("/api/v1/mcp-servers/{id:guid}", async (Guid id, ForgeVaultDbContext db, CancellationToken ct) =>
        {
            var definition = await db.McpServerDefinitions.FindAsync([id], ct);
            return definition is null ? Results.NotFound() : Results.Ok(ToResponse(definition));
        }).RequireAuthorization();

        app.MapDelete("/api/v1/mcp-servers/{id:guid}", async (
            Guid id, ForgeVaultDbContext db, IPermissionChecker permissions, ClaimsPrincipal user, HttpContext http, CancellationToken ct) =>
        {
            var definition = await db.McpServerDefinitions.FindAsync([id], ct);
            if (definition is null)
            {
                return Results.NotFound();
            }

            var callerId = user.GetUserId();
            if (!await permissions.HasPermissionAsync(callerId, Permission.McpRegistryWrite, new ResourceScope(definition.OrganizationId, null, null), ct))
            {
                return Results.Forbid();
            }

            db.McpServerDefinitions.Remove(definition);
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "MCP_SERVER_REMOVED", "mcp_server_definition", definition.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // --- Assignments ---

        app.MapPost("/api/v1/identities/{identityId:guid}/mcp-assignments", async (
            Guid identityId,
            CreateMcpServerAssignmentRequest request,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var definition = await db.McpServerDefinitions.FindAsync([request.McpServerDefinitionId], ct);
            if (definition is null)
            {
                return Results.NotFound(new ErrorResponse("mcp_server_not_found"));
            }

            var callerId = user.GetUserId();
            if (!await permissions.HasPermissionAsync(callerId, Permission.McpRegistryWrite, new ResourceScope(definition.OrganizationId, null, null), ct))
            {
                return Results.Forbid();
            }

            // Associates this MCP grant with the identity's actual RBAC authorization
            // (module 04): wiring an MCP server to an identity that holds no active
            // RoleAssignment anywhere would grant a connection with nothing behind it —
            // reject it outright rather than create an assignment that can never do
            // anything once rendered.
            var activeRoles = await db.RoleAssignments
                .Where(r => r.IdentityId == identityId && r.RevokedAt == null)
                .Select(r => new { r.Id, r.Role, r.ScopeType, r.ScopeId })
                .ToListAsync(ct);
            if (activeRoles.Count == 0)
            {
                return Results.Conflict(new ErrorResponse("identity_has_no_role_assignment"));
            }

            var missingSecretParam = McpAssignmentValidation.FindMissingOrInvalidSecretParam(definition, request.ParamValues);
            if (missingSecretParam is not null)
            {
                return Results.Conflict(new ErrorResponse($"missing_required_secret_param:{missingSecretParam}"));
            }

            var grantAuditMetadata = JsonSerializer.Serialize(new
            {
                grantedViaRoleAssignments = activeRoles.Select(r => new { r.Id, role = r.Role.ToString(), scopeType = r.ScopeType.ToString(), r.ScopeId }),
            });

            var paramValuesJson = request.ParamValues.GetRawText();

            var existing = await db.McpServerAssignments.SingleOrDefaultAsync(a =>
                a.IdentityId == identityId && a.McpServerDefinitionId == request.McpServerDefinitionId && a.RevokedAt == null, ct);
            if (existing is not null)
            {
                existing.ParamValuesJson = paramValuesJson;
                db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "MCP_SERVER_ACCESS_RELEASED", "mcp_server_assignment", existing.Id, http.TraceIdentifier, grantAuditMetadata));
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(existing));
            }

            var assignment = new McpServerAssignment
            {
                Id = Guid.NewGuid(),
                IdentityId = identityId,
                McpServerDefinitionId = request.McpServerDefinitionId,
                ParamValuesJson = paramValuesJson,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.McpServerAssignments.Add(assignment);
            // "MCP_SERVER_ACCESS_RELEASED" — this is the moment access to the MCP server is
            // actually granted to the identity; metadata records exactly which
            // RoleAssignment(s) justified the grant, so the audit trail ties the MCP
            // connection back to the real RBAC authorization behind it, not just "someone
            // with McpRegistryWrite clicked a button."
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "MCP_SERVER_ACCESS_RELEASED", "mcp_server_assignment", assignment.Id, http.TraceIdentifier, grantAuditMetadata));
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/mcp-assignments/{assignment.Id}", ToResponse(assignment));
        }).RequireAuthorization();

        app.MapGet("/api/v1/identities/{identityId:guid}/mcp-assignments", async (
            Guid identityId, ForgeVaultDbContext db, IPermissionChecker permissions, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var callerId = user.GetUserId();
            if (callerId != identityId && !await permissions.HasPermissionAnywhereAsync(callerId, Permission.McpRegistryWrite, ct))
            {
                return Results.Forbid();
            }

            var assignments = await db.McpServerAssignments
                .Where(a => a.IdentityId == identityId)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync(ct);
            return Results.Ok(assignments.Select(ToResponse));
        }).RequireAuthorization();

        app.MapPost("/api/v1/mcp-assignments/{id:guid}/revoke", async (
            Guid id, ForgeVaultDbContext db, IPermissionChecker permissions, ClaimsPrincipal user, HttpContext http, CancellationToken ct) =>
        {
            var assignment = await db.McpServerAssignments.FindAsync([id], ct);
            if (assignment is null)
            {
                return Results.NotFound();
            }

            var definition = await db.McpServerDefinitions.FindAsync([assignment.McpServerDefinitionId], ct);
            var callerId = user.GetUserId();
            if (definition is null || !await permissions.HasPermissionAsync(callerId, Permission.McpRegistryWrite, new ResourceScope(definition.OrganizationId, null, null), ct))
            {
                return Results.Forbid();
            }

            if (assignment.RevokedAt is null)
            {
                assignment.RevokedAt = DateTimeOffset.UtcNow;
                db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "MCP_SERVER_ASSIGNMENT_REVOKED", "mcp_server_assignment", assignment.Id, http.TraceIdentifier));
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(ToResponse(assignment));
        }).RequireAuthorization();

        // --- Render ---

        app.MapGet("/api/v1/mcp-assignments/{id:guid}/render", async (
            Guid id,
            ForgeVaultDbContext db,
            IEnvelopeEncryptionService crypto,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var result = await McpAssignmentRenderer.RenderAsync(
                db, crypto, permissions, id, user.GetUserId(), user.GetActorType(), http.TraceIdentifier, ct);

            return result.Outcome switch
            {
                McpRenderOutcome.Success => Results.Ok(result.Response),
                McpRenderOutcome.Forbidden => Results.Forbid(),
                McpRenderOutcome.AssignmentRevoked => Results.Conflict(new ErrorResponse("assignment_revoked")),
                McpRenderOutcome.ReferencedSecretNotFound => Results.NotFound(new ErrorResponse($"referenced_secret_not_found:{result.Detail}")),
                _ => Results.NotFound(),
            };
        }).RequireAuthorization();
    }

    private static McpServerDefinitionResponse ToResponse(McpServerDefinition definition) => new(
        definition.Id,
        definition.OrganizationId,
        definition.Name,
        definition.Transport.ToString(),
        definition.Command,
        definition.ArgsJson is null ? null : JsonSerializer.Deserialize<List<string>>(definition.ArgsJson),
        definition.Url,
        definition.Timeout,
        definition.ConnectTimeout,
        definition.StaticEnvJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(definition.StaticEnvJson),
        definition.SecretParamNamesJson is null ? null : JsonSerializer.Deserialize<List<string>>(definition.SecretParamNamesJson),
        definition.CreatedAt);

    private static McpServerAssignmentResponse ToResponse(McpServerAssignment assignment) => new(
        assignment.Id,
        assignment.IdentityId,
        assignment.McpServerDefinitionId,
        assignment.RevokedAt is null ? "active" : "revoked",
        JsonDocument.Parse(assignment.ParamValuesJson).RootElement.Clone(),
        assignment.CreatedAt,
        assignment.RevokedAt);
}

public sealed record CreateMcpServerDefinitionRequest(
    string Name,
    McpTransportType Transport,
    string? Command,
    List<string>? Args,
    string? Url,
    int? Timeout,
    int? ConnectTimeout,
    Dictionary<string, string>? StaticEnv,
    List<string>? SecretParamNames);

public sealed record McpServerDefinitionResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Transport,
    string? Command,
    List<string>? Args,
    string? Url,
    int? Timeout,
    int? ConnectTimeout,
    Dictionary<string, string>? StaticEnv,
    List<string>? SecretParamNames,
    DateTimeOffset CreatedAt);

public sealed record CreateMcpServerAssignmentRequest(Guid McpServerDefinitionId, JsonElement ParamValues);

public sealed record McpServerAssignmentResponse(
    Guid Id,
    Guid IdentityId,
    Guid McpServerDefinitionId,
    string Status,
    JsonElement ParamValues,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

public sealed record McpServerRenderResponse(
    string Name,
    string Transport,
    string? Command,
    List<string>? Args,
    string? Url,
    int? Timeout,
    int? ConnectTimeout,
    Dictionary<string, string>? Env,
    Dictionary<string, string>? Headers);
