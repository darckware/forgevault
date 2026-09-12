using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// M8 (docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md): the two REST endpoints added
// alongside the MCP tools of the same name. Same RBAC/no-leak invariants as
// RbacRevealAuditTests — SecretWrite gates revoke, AuditRead (checked "anywhere") gates the
// audit search, and audit rows/responses never contain a secret value.
public sealed class AuditAndRevokeEndpointTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task Revoke_RequiresSecretWrite_IsIdempotent_AndBlocksReveal()
    {
        const string secretValue = "sk-revoke-endpoint-do-not-leak";

        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        var (secretId, _) = await CreateSecretUnderFreshHierarchyAsync(owner, ownerId, secretValue);

        var deniedRevoke = await stranger.PostAsync($"/api/v1/secrets/{secretId}/revoke", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRevoke.StatusCode);

        var revokeResponse = await owner.PostAsync($"/api/v1/secrets/{secretId}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);
        var revoked = await revokeResponse.Content.ReadFromJsonAsync<SecretResponseDto>();
        Assert.Equal("Revoked", revoked!.Status);

        // Idempotent.
        var secondRevoke = await owner.PostAsync($"/api/v1/secrets/{secretId}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, secondRevoke.StatusCode);

        // A revoked secret is denied with 409 (secret_not_active), same as an expired one —
        // distinct from the 403 an RBAC failure produces.
        var revealAttempt = await owner.GetAsync($"/api/v1/secrets/{secretId}/value?mode=REVEAL");
        Assert.Equal(HttpStatusCode.Conflict, revealAttempt.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var revokeLogs = await db.AuditLogs.Where(a => a.ResourceId == secretId && a.Action == "SECRET_REVOKE").ToListAsync();
        Assert.Single(revokeLogs);
    }

    [Fact]
    public async Task Revoke_NonexistentSecret_Is404()
    {
        var (owner, _) = await CreateAuthenticatedClientAsync();

        var response = await owner.PostAsync($"/api/v1/secrets/{Guid.NewGuid()}/revoke", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AuditSearch_RequiresAuditRead_AnywhereNotJustAtScope_AndNeverLeaksSecretValues()
    {
        const string secretValue = "sk-audit-search-do-not-leak";

        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (readOnly, readOnlyId) = await CreateAuthenticatedClientAsync();
        var (auditor, auditorId) = await CreateAuthenticatedClientAsync();

        var orgResponse = await owner.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization!.Id);
        await GrantRoleAsync(readOnlyId, Role.ReadOnly, RoleScopeType.Organization, organization.Id);
        // Auditor is granted at Project scope, not Organization — proves AuditRead is checked
        // "anywhere" for this identity, not against the specific resource's scope chain.
        var projectResponse = await owner.PostAsJsonAsync(
            $"/api/v1/organizations/{organization.Id}/projects", new { name = "proj", slug = "proj" });
        var project = await projectResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(auditorId, Role.Auditor, RoleScopeType.Project, project!.Id);

        var environmentResponse = await owner.PostAsJsonAsync(
            $"/api/v1/projects/{project.Id}/environments", new { name = "Production", slug = "production" });
        var environment = await environmentResponse.Content.ReadFromJsonAsync<IdResponse>();

        var createResponse = await owner.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment!.Id,
            name = "OPENAI_API_KEY",
            type = "ApiKey",
            provider = "openai",
            description = (string?)null,
            value = secretValue,
            expiresAt = (DateTimeOffset?)null,
        });
        var secret = await createResponse.Content.ReadFromJsonAsync<IdResponse>();

        var deniedResponse = await readOnly.GetAsync($"/api/v1/audit?resourceId={secret!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var allowedResponse = await auditor.GetAsync($"/api/v1/audit?resourceId={secret.Id}");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        var body = await allowedResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secretValue, body, StringComparison.Ordinal);

        var page = await allowedResponse.Content.ReadFromJsonAsync<AuditLogPageDto>();
        Assert.True(page!.Total >= 1);
        Assert.All(page.Items, item => Assert.DoesNotContain(secretValue, item.Metadata, StringComparison.Ordinal));
    }

    private async Task<(Guid SecretId, Guid EnvironmentId)> CreateSecretUnderFreshHierarchyAsync(HttpClient client, Guid ownerId, string value)
    {
        var orgResponse = await client.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization!.Id);

        var projectResponse = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization.Id}/projects", new { name = "proj", slug = "proj" });
        var project = await projectResponse.Content.ReadFromJsonAsync<IdResponse>();

        var environmentResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{project!.Id}/environments", new { name = "Production", slug = "production" });
        var environment = await environmentResponse.Content.ReadFromJsonAsync<IdResponse>();

        var createSecretResponse = await client.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment!.Id,
            name = "OPENAI_API_KEY",
            type = "ApiKey",
            provider = "openai",
            description = (string?)null,
            value,
            expiresAt = (DateTimeOffset?)null,
        });
        var secret = await createSecretResponse.Content.ReadFromJsonAsync<IdResponse>();
        return (secret!.Id, environment.Id);
    }

    private async Task GrantRoleAsync(Guid identityId, Role role, RoleScopeType scopeType, Guid scopeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();

        db.RoleAssignments.Add(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            Role = role,
            ScopeType = scopeType,
            ScopeId = scopeId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        const string password = "correct horse battery staple";
        var userId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var now = DateTimeOffset.UtcNow;

            db.Users.Add(new User
            {
                Id = userId,
                Email = email,
                PasswordHash = hasher.Hash(password),
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, userId);
    }

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed record IdResponse(Guid Id);

    private sealed record SecretResponseDto(Guid Id, string Status);

    private sealed record AuditLogItemDto(Guid Id, string ActorId, string Action, string Metadata);

    private sealed record AuditLogPageDto(List<AuditLogItemDto> Items, int Page, int PageSize, int Total);
}
