using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using ForgeVault.Api.Auditing;
using ForgeVault.Api.Endpoints;
using ForgeVault.Application.Authorization;
using ForgeVault.Application.Security;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Auth;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ForgeVault.Api.Mcp;

// M8 (docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md). Every tool here is a thin
// wrapper over the exact same services the REST endpoints in Endpoints/ use — same
// IPermissionChecker scope checks, same AuditLogFactory calls, same crypto service. This is
// deliberate: the MCP surface must never become a second, divergent authorization path
// (docs/ForgeVault.md §160 principle 17 / module 09 §12).
//
// `taskId`/`onBehalfOfAgent`/`runtimeSessionRef` accepted below are the ForgeHub context
// fields that survived a real review of ForgeHub's own Task/TaskExecution model (not the
// speculative `project_id` this module's spec originally guessed at — ForgeHub can't
// provide that without an extra lookup of its own). They are stored in AuditLog.Metadata
// for traceability ONLY — never used for authorization. Per-task/agent ABAC remains Fase 3
// (module 09 §1 decision, closing the module's former open_blocking_questions entry).
[McpServerToolType]
public sealed class VaultTools
{
    [McpServerTool(Name = "secret.metadata")]
    [Description("Returns metadata for a secret. Never returns the value — use credential.request for that.")]
    public static async Task<object> SecretMetadataAsync(
        [Description("The secret's id")] Guid secretId,
        ForgeVaultDbContext db,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var secret = await db.Secrets.FindAsync([secretId], ct);
        if (secret is null)
        {
            throw new McpException("secret_not_found");
        }

        db.AuditLogs.Add(AuditLogFactory.Create(
            user.GetUserId(), user.GetActorType(), "SECRET_READ", "secret", secretId, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return SecretScopeResolver.ToMetadataResponse(secret);
    }

    [McpServerTool(Name = "credential.request")]
    [Description("Requests a secret's value under RBAC. Only accessMode=REVEAL is implemented — BROKER/SESSION/LEASE/INJECT are Fase 2/3 (docs/ForgeVault.md §89).")]
    public static async Task<CredentialEnvelope> CredentialRequestAsync(
        [Description("The secret's id")] Guid secretId,
        ForgeVaultDbContext db,
        IEnvelopeEncryptionService crypto,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Access mode — only REVEAL is implemented today")] string? accessMode = null,
        [Description("ForgeHub task id, for audit traceability only — never used for authorization")] string? taskId = null,
        [Description("Agent/sub-agent identity this request is on behalf of, for audit traceability only")] string? onBehalfOfAgent = null,
        [Description("ForgeHub TaskExecution.runtime_session_ref, for audit traceability only")] string? runtimeSessionRef = null)
    {
        var mode = accessMode ?? "REVEAL";
        if (!string.Equals(mode, "REVEAL", StringComparison.Ordinal))
        {
            throw new McpException("unsupported_access_mode");
        }

        var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
        if (resolved is null)
        {
            throw new McpException("secret_not_found");
        }

        var (secret, scope) = resolved.Value;
        var actorId = user.GetUserId();
        var actorType = user.GetActorType();
        var requestId = NewRequestId();
        var contextFields = new[] { ("taskId", taskId), ("onBehalfOfAgent", onBehalfOfAgent), ("runtimeSessionRef", runtimeSessionRef) };

        // Every attempt is audited, including denials — same invariant as the REST reveal
        // endpoint (docs/modules/06_AUDIT_AND_GOVERNANCE.md §4 invariant 3). Access here is
        // scope RBAC OR a narrower per-secret SecretAccessGrant, same as REST.
        if (!await SecretAccessAuthorizer.CanReadValueAsync(db, permissions, actorId, secretId, scope, ct))
        {
            db.AuditLogs.Add(AuditLogFactory.Create(
                actorId, actorType, "FAILED_ACCESS", "secret", secretId, requestId,
                AuditMetadata.Build([("reason", "forbidden"), .. contextFields])));
            await db.SaveChangesAsync(ct);
            throw new McpException("forbidden");
        }

        var isExpired = secret.ExpiresAt is not null && secret.ExpiresAt <= DateTimeOffset.UtcNow;
        if (secret.Status is not SecretStatus.Active || isExpired)
        {
            db.AuditLogs.Add(AuditLogFactory.Create(
                actorId, actorType, "FAILED_ACCESS", "secret", secretId, requestId,
                AuditMetadata.Build([("reason", "secret_not_active"), .. contextFields])));
            await db.SaveChangesAsync(ct);
            throw new McpException("secret_not_active");
        }

        var currentVersion = await db.SecretVersions
            .Where(v => v.SecretId == secretId && v.Version == secret.CurrentVersion)
            .SingleAsync(ct);

        var payload = new EncryptedPayload(
            currentVersion.Ciphertext, currentVersion.EncryptedDek, currentVersion.Nonce, currentVersion.AuthTag, currentVersion.Algorithm);
        var plaintext = await crypto.DecryptAsync(payload, ct);
        var value = Encoding.UTF8.GetString(plaintext);

        db.AuditLogs.Add(AuditLogFactory.Create(
            actorId, actorType, "SECRET_REVEAL", "secret", secretId, requestId, AuditMetadata.Build(contextFields)));
        await db.SaveChangesAsync(ct);

        return new CredentialEnvelope(
            RequestId: requestId,
            Identity: actorId.ToString(),
            Resource: secret.Id.ToString(),
            AccessMode: "REVEAL",
            ExpiresAt: secret.ExpiresAt,
            Credentials: new { value },
            Session: null,
            Broker: null,
            Lease: null,
            Metadata: new { });
    }

    [McpServerTool(Name = "capability.check")]
    [Description("Checks whether the caller could perform an action on a secret, WITHOUT performing it or revealing anything (docs/ForgeVault.md §138 Policy Simulation, minimal form).")]
    public static async Task<object> CapabilityCheckAsync(
        [Description("The secret id to check against")] Guid secretId,
        [Description("Permission to check: ProjectWrite, EnvironmentWrite, SecretWrite, SecretReadValue, AuditRead")] string permission,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!Enum.TryParse<Permission>(permission, ignoreCase: true, out var parsedPermission))
        {
            throw new McpException("unknown_permission");
        }

        var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
        if (resolved is null)
        {
            throw new McpException("secret_not_found");
        }

        var allowed = await permissions.HasPermissionAsync(user.GetUserId(), parsedPermission, resolved.Value.Scope, ct);
        return new { allowed };
    }

    [McpServerTool(Name = "admin.secret.create")]
    [Description("Creates a secret and its first version. Value is encrypted before storage — mirrors POST /api/v1/secrets.")]
    public static async Task<SecretResponse> AdminSecretCreateAsync(
        [Description("Environment id the secret belongs to")] Guid environmentId,
        [Description("Secret name, convention PROVIDER_RESOURCE_PURPOSE")] string name,
        [Description("Secret type, e.g. ApiKey, DatabaseCredential, GenericSecret")] string type,
        [Description("The plaintext value — encrypted immediately, never logged")] string value,
        ForgeVaultDbContext db,
        IEnvelopeEncryptionService crypto,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Provider name, e.g. openai")] string? provider = null,
        string? description = null,
        DateTimeOffset? expiresAt = null)
    {
        if (!Enum.TryParse<SecretType>(type, ignoreCase: true, out var secretType))
        {
            throw new McpException("unknown_secret_type");
        }

        var environment = await db.Environments
            .Where(e => e.Id == environmentId)
            .Select(e => new { e.Id, e.ProjectId, OrganizationId = e.Project!.OrganizationId })
            .SingleOrDefaultAsync(ct);
        if (environment is null)
        {
            throw new McpException("environment_not_found");
        }

        var actorId = user.GetUserId();
        var scope = new ResourceScope(environment.OrganizationId, environment.ProjectId, environment.Id);
        if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (await db.Secrets.AnyAsync(s => s.EnvironmentId == environmentId && s.Name == name, ct))
        {
            throw new McpException("secret_name_taken");
        }

        var now = DateTimeOffset.UtcNow;
        var secret = new Secret
        {
            Id = Guid.NewGuid(),
            EnvironmentId = environmentId,
            Name = name,
            Type = secretType,
            Provider = provider,
            Description = description,
            OwnerId = actorId,
            Status = SecretStatus.Active,
            CurrentVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = expiresAt,
        };

        var payload = await crypto.EncryptAsync(Encoding.UTF8.GetBytes(value), ct);
        db.Secrets.Add(secret);
        db.SecretVersions.Add(new SecretVersion
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            Version = 1,
            Ciphertext = payload.Ciphertext,
            EncryptedDek = payload.EncryptedDek,
            Nonce = payload.Nonce,
            AuthTag = payload.AuthTag,
            Algorithm = payload.Algorithm,
            CreatedBy = actorId,
            CreatedAt = now,
        });
        db.AuditLogs.Add(AuditLogFactory.Create(actorId, user.GetActorType(), "SECRET_CREATE", "secret", secret.Id, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return SecretScopeResolver.ToMetadataResponse(secret);
    }

    [McpServerTool(Name = "admin.secret.update")]
    [Description("Updates a secret's value, creating a new immutable version — mirrors PUT /api/v1/secrets/{id}.")]
    public static async Task<SecretResponse> AdminSecretUpdateAsync(
        Guid secretId,
        [Description("The new plaintext value — encrypted immediately, never logged")] string value,
        ForgeVaultDbContext db,
        IEnvelopeEncryptionService crypto,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct,
        string? description = null,
        DateTimeOffset? expiresAt = null)
    {
        var (secret, scope) = await ResolveOrThrowAsync(db, secretId, ct);
        var actorId = user.GetUserId();
        if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (secret.Status is SecretStatus.Revoked or SecretStatus.Archived)
        {
            throw new McpException("secret_not_active");
        }

        var now = DateTimeOffset.UtcNow;
        var newVersionNumber = secret.CurrentVersion + 1;
        var payload = await crypto.EncryptAsync(Encoding.UTF8.GetBytes(value), ct);

        db.SecretVersions.Add(new SecretVersion
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            Version = newVersionNumber,
            Ciphertext = payload.Ciphertext,
            EncryptedDek = payload.EncryptedDek,
            Nonce = payload.Nonce,
            AuthTag = payload.AuthTag,
            Algorithm = payload.Algorithm,
            CreatedBy = actorId,
            CreatedAt = now,
        });

        secret.CurrentVersion = newVersionNumber;
        secret.UpdatedAt = now;
        if (description is not null)
        {
            secret.Description = description;
        }

        if (expiresAt is not null)
        {
            secret.ExpiresAt = expiresAt;
        }

        db.AuditLogs.Add(AuditLogFactory.Create(actorId, user.GetActorType(), "SECRET_UPDATE", "secret", secret.Id, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return SecretScopeResolver.ToMetadataResponse(secret);
    }

    [McpServerTool(Name = "admin.secret.rotate")]
    [Description("Issues a new credential value for a secret (SECRET_ROTATE, not SECRET_UPDATE) — mirrors POST /api/v1/secrets/{id}/rotate.")]
    public static async Task<SecretResponse> AdminSecretRotateAsync(
        Guid secretId,
        [Description("The new plaintext value — encrypted immediately, never logged")] string value,
        ForgeVaultDbContext db,
        IEnvelopeEncryptionService crypto,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var (secret, scope) = await ResolveOrThrowAsync(db, secretId, ct);
        var actorId = user.GetUserId();
        if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (secret.Status is SecretStatus.Revoked or SecretStatus.Archived)
        {
            throw new McpException("secret_not_active");
        }

        var now = DateTimeOffset.UtcNow;
        var newVersionNumber = secret.CurrentVersion + 1;
        var payload = await crypto.EncryptAsync(Encoding.UTF8.GetBytes(value), ct);

        db.SecretVersions.Add(new SecretVersion
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            Version = newVersionNumber,
            Ciphertext = payload.Ciphertext,
            EncryptedDek = payload.EncryptedDek,
            Nonce = payload.Nonce,
            AuthTag = payload.AuthTag,
            Algorithm = payload.Algorithm,
            CreatedBy = actorId,
            CreatedAt = now,
        });

        secret.CurrentVersion = newVersionNumber;
        secret.UpdatedAt = now;

        db.AuditLogs.Add(AuditLogFactory.Create(actorId, user.GetActorType(), "SECRET_ROTATE", "secret", secret.Id, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return SecretScopeResolver.ToMetadataResponse(secret);
    }

    [McpServerTool(Name = "admin.secret.revoke")]
    [Description("Revokes a secret — no further read/write/reveal/rotate succeeds — and cascades to every SecretAccessGrant/McpServerAssignment referencing it (module 07). Idempotent. Mirrors POST /api/v1/secrets/{id}/revoke.")]
    public static async Task<SecretRevokeResponse> AdminSecretRevokeAsync(
        Guid secretId,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var (secret, scope) = await ResolveOrThrowAsync(db, secretId, ct);
        var actorId = user.GetUserId();
        if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (secret.Status is SecretStatus.Revoked)
        {
            return SecretEndpoints.ToRevokeResponse(secret, 0, 0);
        }

        var now = DateTimeOffset.UtcNow;
        secret.Status = SecretStatus.Revoked;
        secret.UpdatedAt = now;

        var revokedGrants = await db.SecretAccessGrants
            .Where(g => g.SecretId == secretId && g.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var grant in revokedGrants)
        {
            grant.RevokedAt = now;
        }

        var revokedAssignments = await SecretEndpoints.RevokeMcpAssignmentsReferencingSecretAsync(db, secretId, now, ct);

        db.AuditLogs.Add(AuditLogFactory.Create(
            actorId, user.GetActorType(), "SECRET_REVOKE", "secret", secret.Id, NewRequestId(),
            $$"""{"revokedAccessGrants":{{revokedGrants.Count}},"revokedMcpAssignments":{{revokedAssignments}}}"""));
        await db.SaveChangesAsync(ct);

        return SecretEndpoints.ToRevokeResponse(secret, revokedGrants.Count, revokedAssignments);
    }

    [McpServerTool(Name = "admin.secret.impact")]
    [Description("Lists who would be affected by rotating or revoking this secret: identities with a role granting SecretReadValue in its scope, active SecretAccessGrant identities, and active McpServerAssignment ids referencing it. RBAC gated by SecretWrite. Mirrors GET /api/v1/secrets/{id}/impact.")]
    public static async Task<SecretImpactResponse> AdminSecretImpactAsync(
        Guid secretId,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var (_, scope) = await ResolveOrThrowAsync(db, secretId, ct);
        if (!await permissions.HasPermissionAsync(user.GetUserId(), Permission.SecretWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        var roleConsumers = await permissions.ListIdentitiesWithPermissionAsync(Permission.SecretReadValue, scope, ct);
        var accessGrantIdentityIds = await db.SecretAccessGrants
            .Where(g => g.SecretId == secretId && g.RevokedAt == null)
            .Select(g => g.IdentityId)
            .ToListAsync(ct);
        var mcpAssignmentIds = await SecretEndpoints.FindMcpAssignmentIdsReferencingSecretAsync(db, secretId, ct);

        return new SecretImpactResponse(
            secretId,
            roleConsumers.Select(r => new RoleConsumer(r.IdentityId, r.Role)).ToList(),
            accessGrantIdentityIds,
            mcpAssignmentIds);
    }

    [McpServerTool(Name = "admin.secret.grant_access")]
    [Description("Grants a single identity (User or ServiceAccount) read access to exactly this secret, independent of any RoleAssignment scope — for handing an agent one credential (a site login, a database credential, a provider token) without also granting everything else in the Environment. RBAC gated by SecretWrite at the secret's scope. Idempotent. Mirrors POST /api/v1/secrets/{id}/access-grants.")]
    public static async Task<object> AdminSecretGrantAccessAsync(
        [Description("The secret's id")] Guid secretId,
        [Description("Identity (User or ServiceAccount id) to grant read access to")] Guid identityId,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var (_, scope) = await ResolveOrThrowAsync(db, secretId, ct);
        var callerId = user.GetUserId();
        if (!await permissions.HasPermissionAsync(callerId, Permission.SecretWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        var existing = await db.SecretAccessGrants.SingleOrDefaultAsync(
            g => g.SecretId == secretId && g.IdentityId == identityId && g.RevokedAt == null, ct);
        if (existing is not null)
        {
            return ToSecretAccessGrantObject(existing);
        }

        var grant = new SecretAccessGrant
        {
            Id = Guid.NewGuid(),
            SecretId = secretId,
            IdentityId = identityId,
            GrantedBy = callerId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.SecretAccessGrants.Add(grant);
        db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "SECRET_ACCESS_GRANTED", "secret_access_grant", grant.Id, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return ToSecretAccessGrantObject(grant);
    }

    [McpServerTool(Name = "admin.secret.revoke_access")]
    [Description("Revokes a SecretAccessGrant by id — the identity immediately loses that specific credential's access (any other RoleAssignment it holds is unaffected). Idempotent. Mirrors POST /api/v1/secret-access-grants/{id}/revoke.")]
    public static async Task<object> AdminSecretRevokeAccessAsync(
        Guid secretAccessGrantId,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var grant = await db.SecretAccessGrants.FindAsync([secretAccessGrantId], ct);
        if (grant is null)
        {
            throw new McpException("secret_access_grant_not_found");
        }

        var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, grant.SecretId, ct);
        var callerId = user.GetUserId();
        if (resolved is null || !await permissions.HasPermissionAsync(callerId, Permission.SecretWrite, resolved.Value.Scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (grant.RevokedAt is null)
        {
            grant.RevokedAt = DateTimeOffset.UtcNow;
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "SECRET_ACCESS_REVOKED", "secret_access_grant", grant.Id, NewRequestId()));
            await db.SaveChangesAsync(ct);
        }

        return ToSecretAccessGrantObject(grant);
    }

    [McpServerTool(Name = "admin.audit.search")]
    [Description("Searches the audit trail. Never returns secret values — audit rows never contain them. Mirrors GET /api/v1/audit.")]
    public static async Task<AuditLogPage> AdminAuditSearchAsync(
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct,
        string? actorId = null,
        string? resourceType = null,
        Guid? resourceId = null,
        string? action = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? page = null,
        int? pageSize = null)
    {
        if (!await permissions.HasPermissionAnywhereAsync(user.GetUserId(), Permission.AuditRead, ct))
        {
            throw new McpException("forbidden");
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

        return new AuditLogPage(items, effectivePage, effectivePageSize, total);
    }

    [McpServerTool(Name = "admin.role.grant")]
    [Description("Grants a role to an identity (a User or ServiceAccount id) at a scope (Organization/Project/Environment). RBAC gated by RoleAssignmentWrite (Owner/Admin) at that scope. Idempotent for the same (identity, role, scope).")]
    public static async Task<object> AdminRoleGrantAsync(
        [Description("Identity to grant the role to (a User or ServiceAccount id)")] Guid identityId,
        [Description("Role name: Owner, Admin, SecurityAdmin, ProjectAdmin, Developer, Operator, Auditor, ReadOnly, Agent, ServiceAccount")] string role,
        [Description("Scope type: Organization, Project, or Environment")] string scopeType,
        [Description("Id of the Organization/Project/Environment matching scopeType")] Guid scopeId,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var (parsedRole, parsedScopeType) = ParseRoleAndScopeTypeOrThrow(role, scopeType);

        var scope = await RoleAssignmentScopeResolver.ResolveAsync(db, parsedScopeType, scopeId, ct);
        if (scope is null)
        {
            throw new McpException("scope_not_found");
        }

        var callerId = user.GetUserId();
        if (!await permissions.HasPermissionAsync(callerId, Permission.RoleAssignmentWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        // Idempotency by (identity_id, role, scope_type, scope_id) — module 04 §7.
        var existing = await db.RoleAssignments.SingleOrDefaultAsync(r =>
            r.IdentityId == identityId && r.Role == parsedRole &&
            r.ScopeType == parsedScopeType && r.ScopeId == scopeId &&
            r.RevokedAt == null, ct);
        if (existing is not null)
        {
            return ToRoleAssignmentObject(existing);
        }

        var assignment = new RoleAssignment
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            Role = parsedRole,
            ScopeType = parsedScopeType,
            ScopeId = scopeId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.RoleAssignments.Add(assignment);
        db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "ACCESS_GRANTED", "role_assignment", assignment.Id, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return ToRoleAssignmentObject(assignment);
    }

    [McpServerTool(Name = "admin.role.revoke")]
    [Description("Revokes a RoleAssignment by id — the identity immediately loses whatever that grant provided. Idempotent.")]
    public static async Task<object> AdminRoleRevokeAsync(
        Guid roleAssignmentId,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var assignment = await db.RoleAssignments.FindAsync([roleAssignmentId], ct);
        if (assignment is null)
        {
            throw new McpException("role_assignment_not_found");
        }

        var scope = await RoleAssignmentScopeResolver.ResolveAsync(db, assignment.ScopeType, assignment.ScopeId, ct);
        var callerId = user.GetUserId();
        if (scope is null || !await permissions.HasPermissionAsync(callerId, Permission.RoleAssignmentWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (assignment.RevokedAt is null)
        {
            assignment.RevokedAt = DateTimeOffset.UtcNow;
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "ACCESS_REVOKED", "role_assignment", assignment.Id, NewRequestId()));
            await db.SaveChangesAsync(ct);
        }

        return ToRoleAssignmentObject(assignment);
    }

    [McpServerTool(Name = "admin.agent.register")]
    [Description("Onboards a new agent in one call: creates its ServiceAccount identity, issues its fv_sa_... token (shown once, store it immediately), and grants it a role at a scope. Callable only by an identity holding RoleAssignmentWrite (Owner/Admin) at that scope — an agent can never register itself. Once registered, the agent uses its own token to call admin.secret.create/credential.request for the credentials it already holds.")]
    public static async Task<object> AdminAgentRegisterAsync(
        [Description("Agent name — becomes the ServiceAccount name, must be unique")] string name,
        [Description("Scope type to grant access at: Organization, Project, or Environment")] string scopeType,
        [Description("Id of the Organization/Project/Environment matching scopeType")] Guid scopeId,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Role to grant the agent — defaults to Agent")] string role = "Agent")
    {
        var (parsedRole, parsedScopeType) = ParseRoleAndScopeTypeOrThrow(role, scopeType);

        var scope = await RoleAssignmentScopeResolver.ResolveAsync(db, parsedScopeType, scopeId, ct);
        if (scope is null)
        {
            throw new McpException("scope_not_found");
        }

        var callerId = user.GetUserId();
        var callerType = user.GetActorType();
        if (!await permissions.HasPermissionAsync(callerId, Permission.RoleAssignmentWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (await db.ServiceAccounts.AnyAsync(s => s.Name == name, ct))
        {
            throw new McpException("service_account_name_taken");
        }

        var now = DateTimeOffset.UtcNow;
        var account = new ServiceAccount { Id = Guid.NewGuid(), Name = name, IsActive = true, CreatedAt = now };
        db.ServiceAccounts.Add(account);
        db.AuditLogs.Add(AuditLogFactory.Create(callerId, callerType, "SERVICE_ACCOUNT_CREATE", "service_account", account.Id, NewRequestId()));

        var rawToken = ServiceAccountTokenFactory.GenerateRawToken();
        var token = new ServiceAccountToken
        {
            Id = Guid.NewGuid(),
            ServiceAccountId = account.Id,
            TokenHash = ServiceAccountTokenFactory.Hash(rawToken),
            TokenPrefix = ServiceAccountTokenFactory.ToDisplayPrefix(rawToken),
            IssuedAt = now,
        };
        db.ServiceAccountTokens.Add(token);
        db.AuditLogs.Add(AuditLogFactory.Create(callerId, callerType, "TOKEN_ISSUED", "service_account", account.Id, NewRequestId()));

        var assignment = new RoleAssignment
        {
            Id = Guid.NewGuid(),
            IdentityId = account.Id,
            Role = parsedRole,
            ScopeType = parsedScopeType,
            ScopeId = scopeId,
            CreatedAt = now,
        };
        db.RoleAssignments.Add(assignment);
        db.AuditLogs.Add(AuditLogFactory.Create(callerId, callerType, "ACCESS_GRANTED", "role_assignment", assignment.Id, NewRequestId()));

        await db.SaveChangesAsync(ct);

        return new
        {
            serviceAccountId = account.Id,
            name = account.Name,
            token = rawToken,
            tokenPrefix = token.TokenPrefix,
            roleAssignmentId = assignment.Id,
            role = parsedRole.ToString(),
            scopeType = parsedScopeType.ToString(),
            scopeId,
        };
    }

    private static (Role Role, RoleScopeType ScopeType) ParseRoleAndScopeTypeOrThrow(string role, string scopeType)
    {
        if (!Enum.TryParse<Role>(role, ignoreCase: true, out var parsedRole))
        {
            throw new McpException("unknown_role");
        }

        if (!Enum.TryParse<RoleScopeType>(scopeType, ignoreCase: true, out var parsedScopeType))
        {
            throw new McpException("unknown_scope_type");
        }

        return (parsedRole, parsedScopeType);
    }

    private static object ToRoleAssignmentObject(RoleAssignment assignment) => new
    {
        id = assignment.Id,
        identityId = assignment.IdentityId,
        role = assignment.Role.ToString(),
        scopeType = assignment.ScopeType.ToString(),
        scopeId = assignment.ScopeId,
        status = assignment.RevokedAt is null ? "active" : "revoked",
        createdAt = assignment.CreatedAt,
        revokedAt = assignment.RevokedAt,
    };

    private static object ToSecretAccessGrantObject(SecretAccessGrant grant) => new
    {
        id = grant.Id,
        secretId = grant.SecretId,
        identityId = grant.IdentityId,
        grantedBy = grant.GrantedBy,
        status = grant.RevokedAt is null ? "active" : "revoked",
        createdAt = grant.CreatedAt,
        revokedAt = grant.RevokedAt,
    };

    [McpServerTool(Name = "admin.mcp.register")]
    [Description("Registers a new MCP server definition in the catalog (e.g. a stdio server like 'forgehub-messages' or an HTTP one like 'forgevault' itself) under an Organization. RBAC gated by McpRegistryWrite (Owner/Admin).")]
    public static async Task<object> AdminMcpRegisterAsync(
        [Description("Organization this definition belongs to")] Guid organizationId,
        [Description("Unique name within the organization, e.g. 'forgehub-messages'")] string name,
        [Description("Transport: Stdio or Http")] string transport,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Stdio only: the executable, e.g. 'uv'")] string? command = null,
        [Description("Stdio only: JSON array of args, e.g. [\"run\", \"/path/script.py\"]")] string? argsJson = null,
        [Description("Http only: the MCP endpoint URL")] string? url = null,
        int? timeout = null,
        int? connectTimeout = null,
        [Description("JSON dict of non-sensitive static env/config, e.g. {\"FORGEHUB_API_URL\":\"http://localhost:8000\"}")] string? staticEnvJson = null,
        [Description("JSON array of parameter names each assignment must supply as a Secret reference, e.g. [\"FORGEHUB_AGENT_TOKEN\"]")] string? secretParamNamesJson = null)
    {
        if (!Enum.TryParse<McpTransportType>(transport, ignoreCase: true, out var parsedTransport))
        {
            throw new McpException("unknown_transport");
        }

        if (!await db.Organizations.AnyAsync(o => o.Id == organizationId, ct))
        {
            throw new McpException("organization_not_found");
        }

        var callerId = user.GetUserId();
        if (!await permissions.HasPermissionAsync(callerId, Permission.McpRegistryWrite, new ResourceScope(organizationId, null, null), ct))
        {
            throw new McpException("forbidden");
        }

        if (await db.McpServerDefinitions.AnyAsync(m => m.OrganizationId == organizationId && m.Name == name, ct))
        {
            throw new McpException("mcp_server_name_taken");
        }

        var definition = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Transport = parsedTransport,
            Command = command,
            ArgsJson = argsJson,
            Url = url,
            Timeout = timeout,
            ConnectTimeout = connectTimeout,
            StaticEnvJson = staticEnvJson,
            SecretParamNamesJson = secretParamNamesJson,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.McpServerDefinitions.Add(definition);
        db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "MCP_SERVER_REGISTERED", "mcp_server_definition", definition.Id, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return new { id = definition.Id, organizationId, name, transport = parsedTransport.ToString() };
    }

    [McpServerTool(Name = "admin.mcp.assign")]
    [Description("Grants an identity access to a registered MCP server, binding it to that identity's real RBAC authorization (rejected if the identity holds no active RoleAssignment anywhere). paramValuesJson is a JSON dict where each value is either a literal string or {\"secretId\":\"<guid>\"} referencing an existing Secret. RBAC gated by McpRegistryWrite. Idempotent per (identity, definition).")]
    public static async Task<object> AdminMcpAssignAsync(
        Guid identityId,
        Guid mcpServerDefinitionId,
        [Description("JSON dict: literal strings for non-sensitive values, {\"secretId\":\"...\"} for values that must come from an existing Secret")] string paramValuesJson,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var definition = await db.McpServerDefinitions.FindAsync([mcpServerDefinitionId], ct);
        if (definition is null)
        {
            throw new McpException("mcp_server_not_found");
        }

        var callerId = user.GetUserId();
        if (!await permissions.HasPermissionAsync(callerId, Permission.McpRegistryWrite, new ResourceScope(definition.OrganizationId, null, null), ct))
        {
            throw new McpException("forbidden");
        }

        var activeRoles = await db.RoleAssignments
            .Where(r => r.IdentityId == identityId && r.RevokedAt == null)
            .Select(r => new { r.Id, r.Role, r.ScopeType, r.ScopeId })
            .ToListAsync(ct);
        if (activeRoles.Count == 0)
        {
            throw new McpException("identity_has_no_role_assignment");
        }

        using var paramValuesDoc = System.Text.Json.JsonDocument.Parse(paramValuesJson);
        var missingSecretParam = McpAssignmentValidation.FindMissingOrInvalidSecretParam(definition, paramValuesDoc.RootElement);
        if (missingSecretParam is not null)
        {
            throw new McpException($"missing_required_secret_param:{missingSecretParam}");
        }

        var grantAuditMetadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            grantedViaRoleAssignments = activeRoles.Select(r => new { r.Id, role = r.Role.ToString(), scopeType = r.ScopeType.ToString(), r.ScopeId }),
        });

        var existing = await db.McpServerAssignments.SingleOrDefaultAsync(a =>
            a.IdentityId == identityId && a.McpServerDefinitionId == mcpServerDefinitionId && a.RevokedAt == null, ct);
        if (existing is not null)
        {
            existing.ParamValuesJson = paramValuesJson;
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "MCP_SERVER_ACCESS_RELEASED", "mcp_server_assignment", existing.Id, NewRequestId(), grantAuditMetadata));
            await db.SaveChangesAsync(ct);
            return ToMcpAssignmentObject(existing);
        }

        var assignment = new McpServerAssignment
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            McpServerDefinitionId = mcpServerDefinitionId,
            ParamValuesJson = paramValuesJson,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.McpServerAssignments.Add(assignment);
        db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "MCP_SERVER_ACCESS_RELEASED", "mcp_server_assignment", assignment.Id, NewRequestId(), grantAuditMetadata));
        await db.SaveChangesAsync(ct);

        return ToMcpAssignmentObject(assignment);
    }

    [McpServerTool(Name = "admin.mcp.revoke_assignment")]
    [Description("Revokes an identity's access to an MCP server. Idempotent.")]
    public static async Task<object> AdminMcpRevokeAssignmentAsync(
        Guid mcpAssignmentId,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var assignment = await db.McpServerAssignments.FindAsync([mcpAssignmentId], ct);
        if (assignment is null)
        {
            throw new McpException("mcp_assignment_not_found");
        }

        var definition = await db.McpServerDefinitions.FindAsync([assignment.McpServerDefinitionId], ct);
        var callerId = user.GetUserId();
        if (definition is null || !await permissions.HasPermissionAsync(callerId, Permission.McpRegistryWrite, new ResourceScope(definition.OrganizationId, null, null), ct))
        {
            throw new McpException("forbidden");
        }

        if (assignment.RevokedAt is null)
        {
            assignment.RevokedAt = DateTimeOffset.UtcNow;
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, user.GetActorType(), "MCP_SERVER_ASSIGNMENT_REVOKED", "mcp_server_assignment", assignment.Id, NewRequestId()));
            await db.SaveChangesAsync(ct);
        }

        return ToMcpAssignmentObject(assignment);
    }

    [McpServerTool(Name = "mcp.render_config")]
    [Description("Resolves an MCP server assignment into the ready-to-use config block (with any referenced Secret decrypted) — what deploy/scripts/sync_mcp_config.py writes into an agent's mcp_servers: YAML. Callable by the assignment's own identity for itself, or by anyone holding SecretReadValue on every referenced secret.")]
    public static async Task<object> McpRenderConfigAsync(
        Guid mcpAssignmentId,
        ForgeVaultDbContext db,
        IEnvelopeEncryptionService crypto,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var result = await McpAssignmentRenderer.RenderAsync(
            db, crypto, permissions, mcpAssignmentId, user.GetUserId(), user.GetActorType(), NewRequestId(), ct);

        return result.Outcome switch
        {
            McpRenderOutcome.Success => result.Response!,
            McpRenderOutcome.Forbidden => throw new McpException("forbidden"),
            McpRenderOutcome.AssignmentRevoked => throw new McpException("assignment_revoked"),
            McpRenderOutcome.ReferencedSecretNotFound => throw new McpException($"referenced_secret_not_found:{result.Detail}"),
            McpRenderOutcome.AssignmentNotFound => throw new McpException("mcp_assignment_not_found"),
            _ => throw new McpException("mcp_server_not_found"),
        };
    }

    private static object ToMcpAssignmentObject(McpServerAssignment assignment) => new
    {
        id = assignment.Id,
        identityId = assignment.IdentityId,
        mcpServerDefinitionId = assignment.McpServerDefinitionId,
        status = assignment.RevokedAt is null ? "active" : "revoked",
        createdAt = assignment.CreatedAt,
        revokedAt = assignment.RevokedAt,
    };

    private static async Task<(Secret Secret, ResourceScope Scope)> ResolveOrThrowAsync(
        ForgeVaultDbContext db, Guid secretId, CancellationToken ct)
    {
        var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
        if (resolved is null)
        {
            throw new McpException("secret_not_found");
        }

        return resolved.Value;
    }

    private static string NewRequestId() => Guid.NewGuid().ToString();
}
