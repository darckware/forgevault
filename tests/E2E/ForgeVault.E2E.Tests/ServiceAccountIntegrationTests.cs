using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.E2E.Tests;

// docs/architecture/IMPLEMENTATION_READINESS.md, milestone M7 "done when": a
// service:forgehub identity can authenticate, and a service:forgerouter identity can
// fetch a credential value under RBAC — the two remaining §62 acceptance criteria
// ("ForgeHub consegue autenticar", "ForgeRouter consegue recuperar credencial").
public sealed class ServiceAccountIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ServiceAccount_AuthenticatesWithBearerToken_AndRejectsAnInvalidOne()
    {
        var admin = await CreateAuthenticatedAdminAsync();
        var (_, rawToken) = await CreateServiceAccountWithTokenAsync(admin, "forgehub");

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);

        // Any authenticated endpoint proves the identity authenticated successfully —
        // this mirrors "ForgeHub consegue autenticar" from docs/ForgeVault.md §62.
        var response = await client.GetAsync("/api/v1/secrets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var withBadToken = factory.CreateClient();
        withBadToken.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fv_sa_not-a-real-token");
        var badResponse = await withBadToken.GetAsync("/api/v1/secrets");
        Assert.Equal(HttpStatusCode.Unauthorized, badResponse.StatusCode);
    }

    [Fact]
    public async Task ServiceAccount_RevokedToken_IsRejected()
    {
        var admin = await CreateAuthenticatedAdminAsync();
        var (serviceAccountId, rawToken) = await CreateServiceAccountWithTokenAsync(admin, "temp-integration");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
            var token = await db.ServiceAccountTokens.SingleAsync(t => t.ServiceAccountId == serviceAccountId);
            token.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        var response = await client.GetAsync("/api/v1/secrets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ServiceAccount_WithSecretReadValuePermission_RevealsCredential_WithoutItGets403()
    {
        var admin = await CreateAuthenticatedAdminAsync();

        var orgResponse = await admin.Client.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(admin.UserId, Role.Owner, RoleScopeType.Organization, organization!.Id);

        var projectResponse = await admin.Client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization.Id}/projects", new { name = "forgerouter-project", slug = "forgerouter-project" });
        var project = await projectResponse.Content.ReadFromJsonAsync<IdResponse>();

        var environmentResponse = await admin.Client.PostAsJsonAsync(
            $"/api/v1/projects/{project!.Id}/environments", new { name = "Production", slug = "production" });
        var environment = await environmentResponse.Content.ReadFromJsonAsync<IdResponse>();

        const string credentialValue = "sk-openai-for-forgerouter";
        var createSecretResponse = await admin.Client.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment!.Id,
            name = "OPENAI_API_KEY",
            type = "ApiKey",
            provider = "openai",
            description = (string?)null,
            value = credentialValue,
            expiresAt = (DateTimeOffset?)null,
        });
        var secret = await createSecretResponse.Content.ReadFromJsonAsync<IdResponse>();

        var (forgeRouterId, forgeRouterToken) = await CreateServiceAccountWithTokenAsync(admin, "forgerouter");
        var forgeRouterClient = factory.CreateClient();
        forgeRouterClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgeRouterToken);

        // Without a grant, RBAC denies a service identity exactly like it would a human one.
        var deniedResponse = await forgeRouterClient.GetAsync($"/api/v1/secrets/{secret!.Id}/value?mode=REVEAL");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        await GrantRoleAsync(forgeRouterId, Role.ServiceAccount, RoleScopeType.Organization, organization.Id);

        var allowedResponse = await forgeRouterClient.GetAsync($"/api/v1/secrets/{secret.Id}/value?mode=REVEAL");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        var envelope = await allowedResponse.Content.ReadFromJsonAsync<CredentialEnvelopeDto>();
        Assert.Equal(credentialValue, envelope!.Credentials.GetProperty("value").GetString());

        // The audit trail records the service account itself as the actor, not the admin
        // who provisioned it (docs/modules/06_AUDIT_AND_GOVERNANCE.md).
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var revealLog = await db.AuditLogs
            .SingleAsync(a => a.ResourceId == secret.Id && a.Action == "SECRET_REVEAL");
        Assert.Equal(forgeRouterId.ToString(), revealLog.ActorId);
        Assert.Equal(AuditActorType.Service, revealLog.ActorType);
    }

    private async Task<(Guid ServiceAccountId, string RawToken)> CreateServiceAccountWithTokenAsync(
        (HttpClient Client, Guid UserId) admin, string name)
    {
        var createResponse = await admin.Client.PostAsJsonAsync(
            "/api/v1/service-accounts", new { name = $"{name}-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var account = await createResponse.Content.ReadFromJsonAsync<IdResponse>();

        var tokenResponse = await admin.Client.PostAsync($"/api/v1/service-accounts/{account!.Id}/tokens", content: null);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = await tokenResponse.Content.ReadFromJsonAsync<ServiceAccountTokenResponseDto>();
        Assert.StartsWith("fv_sa_", token!.Token, StringComparison.Ordinal);

        return (account.Id, token.Token);
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

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedAdminAsync()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.test";
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

    private sealed record ServiceAccountTokenResponseDto(string Token, string TokenPrefix, DateTimeOffset IssuedAt);

    private sealed record CredentialEnvelopeDto(string AccessMode, System.Text.Json.JsonElement Credentials);
}
