using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// 2026-10-16: creating a service account and minting tokens for one were [Authorize]-only,
// so any authenticated identity could mint a token for ANOTHER account and act with its
// roles. Both now require UserManage; revoking is allowed to admins and to the account itself.
public sealed class ServiceAccountTokenAuthorizationTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task Only_identity_admins_create_accounts_and_mint_tokens()
    {
        var helper = new ServiceAccountDeleteTests(factory);
        var (owner, ownerId) = await helper.CreateAuthenticatedClientAsync();
        var (stranger, _) = await helper.CreateAuthenticatedClientAsync();
        var ownerOrganizationId = await helper.GrantOwnerAtNewOrganizationAsync(owner, ownerId);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await stranger.PostAsJsonAsync("/api/v1/service-accounts", new { name = $"x-{Guid.NewGuid():N}" })).StatusCode);

        var created = await owner.PostAsJsonAsync("/api/v1/service-accounts", new { name = $"victim-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var accountId = (await created.Content.ReadFromJsonAsync<ServiceAccountDeleteTests.IdResponse>())!.Id;
        await GrantAccountRoleAsync(accountId, ownerOrganizationId);

        // The escalation path that was open: a non-admin minting a token for someone else.
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/api/v1/service-accounts/{accountId}/tokens", null)).StatusCode);

        var issued = await owner.PostAsync($"/api/v1/service-accounts/{accountId}/tokens", null);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var raw = (await issued.Content.ReadFromJsonAsync<TokenResponse>())!.Token;

        // The account itself can retire its own token; a stranger can't.
        using var self = factory.CreateClient();
        self.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", raw);
        var tokenId = await helper.TokenIdForAsync(accountId);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/api/v1/service-accounts/{accountId}/tokens/{tokenId}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await self.PostAsync($"/api/v1/service-accounts/{accountId}/tokens/{tokenId}/revoke", null)).StatusCode);
    }

    [Fact]
    public async Task Token_issuance_requires_permission_for_every_active_account_scope()
    {
        var helper = new ServiceAccountDeleteTests(factory);
        var (firstAdmin, firstAdminId) = await helper.CreateAuthenticatedClientAsync();
        var (secondAdmin, secondAdminId) = await helper.CreateAuthenticatedClientAsync();
        var firstOrg = await helper.GrantOwnerAtNewOrganizationAsync(firstAdmin, firstAdminId);
        var secondOrg = await helper.GrantOwnerAtNewOrganizationAsync(secondAdmin, secondAdminId);

        var created = await firstAdmin.PostAsJsonAsync("/api/v1/service-accounts", new { name = $"scoped-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var accountId = (await created.Content.ReadFromJsonAsync<ServiceAccountDeleteTests.IdResponse>())!.Id;
        await GrantAccountRoleAsync(accountId, secondOrg);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await firstAdmin.PostAsync($"/api/v1/service-accounts/{accountId}/tokens", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await secondAdmin.PostAsync($"/api/v1/service-accounts/{accountId}/tokens", null)).StatusCode);

        await GrantAccountRoleAsync(accountId, firstOrg);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await secondAdmin.PostAsync($"/api/v1/service-accounts/{accountId}/tokens", null)).StatusCode);
    }

    [Fact]
    public async Task Token_issuance_checks_direct_secret_grants_too()
    {
        var helper = new ServiceAccountDeleteTests(factory);
        var (firstAdmin, firstAdminId) = await helper.CreateAuthenticatedClientAsync();
        var (secondAdmin, secondAdminId) = await helper.CreateAuthenticatedClientAsync();
        var firstOrg = await helper.GrantOwnerAtNewOrganizationAsync(firstAdmin, firstAdminId);
        var secondOrg = await helper.GrantOwnerAtNewOrganizationAsync(secondAdmin, secondAdminId);
        var created = await firstAdmin.PostAsJsonAsync("/api/v1/service-accounts", new { name = $"grant-target-{Guid.NewGuid():N}" });
        var accountId = (await created.Content.ReadFromJsonAsync<ServiceAccountDeleteTests.IdResponse>())!.Id;
        await GrantAccountRoleAsync(accountId, firstOrg);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
            var projectId = Guid.NewGuid();
            var environmentId = Guid.NewGuid();
            var secretId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            db.Projects.Add(new Project { Id = projectId, OrganizationId = secondOrg, Name = "grant test", Slug = $"grant-{Guid.NewGuid():N}", CreatedAt = now, UpdatedAt = now });
            db.Environments.Add(new ForgeVault.Domain.Entities.Environment { Id = environmentId, ProjectId = projectId, Name = EnvironmentKind.Production, Slug = "production", CreatedAt = now });
            db.Secrets.Add(new Secret { Id = secretId, EnvironmentId = environmentId, Name = "TEST_GRANT", OwnerId = secondAdminId, CreatedAt = now, UpdatedAt = now });
            db.SecretAccessGrants.Add(new SecretAccessGrant { Id = Guid.NewGuid(), SecretId = secretId, IdentityId = accountId, GrantedBy = secondAdminId, CreatedAt = now });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await firstAdmin.PostAsync($"/api/v1/service-accounts/{accountId}/tokens", null)).StatusCode);
    }

    [Fact]
    public async Task Token_issuance_checks_mcp_assignment_scope_too()
    {
        var helper = new ServiceAccountDeleteTests(factory);
        var (firstAdmin, firstAdminId) = await helper.CreateAuthenticatedClientAsync();
        var (secondAdmin, secondAdminId) = await helper.CreateAuthenticatedClientAsync();
        var firstOrg = await helper.GrantOwnerAtNewOrganizationAsync(firstAdmin, firstAdminId);
        var secondOrg = await helper.GrantOwnerAtNewOrganizationAsync(secondAdmin, secondAdminId);
        var created = await firstAdmin.PostAsJsonAsync("/api/v1/service-accounts", new { name = $"mcp-target-{Guid.NewGuid():N}" });
        var accountId = (await created.Content.ReadFromJsonAsync<ServiceAccountDeleteTests.IdResponse>())!.Id;
        await GrantAccountRoleAsync(accountId, firstOrg);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
            var definitionId = Guid.NewGuid();
            db.McpServerDefinitions.Add(new McpServerDefinition
            {
                Id = definitionId, OrganizationId = secondOrg, Name = $"test-{Guid.NewGuid():N}",
                Transport = McpTransportType.Http, Url = "http://localhost/unused", CreatedAt = DateTimeOffset.UtcNow,
            });
            db.McpServerAssignments.Add(new McpServerAssignment
            {
                Id = Guid.NewGuid(), IdentityId = accountId, McpServerDefinitionId = definitionId,
                ParamValuesJson = "{}", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await firstAdmin.PostAsync($"/api/v1/service-accounts/{accountId}/tokens", null)).StatusCode);
    }

    private async Task GrantAccountRoleAsync(Guid accountId, Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        db.RoleAssignments.Add(new RoleAssignment
        {
            Id = Guid.NewGuid(), IdentityId = accountId, Role = Role.Agent,
            ScopeType = RoleScopeType.Organization, ScopeId = organizationId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private sealed record TokenResponse(string Token);
}
