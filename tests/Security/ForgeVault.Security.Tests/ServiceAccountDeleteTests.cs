using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// DELETE /api/v1/service-accounts/{id} (2026-10-14). Same invariants as the User delete:
// gated by UserManage, and the identity's grants are revoked (audited) rather than left
// dangling, since they reference it by a soft IdentityId. Its tokens go with the row.
public sealed class ServiceAccountDeleteTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task Delete_RemovesAccountAndTokens_RevokesRoles_RequiresUserManage()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        var organizationId = await GrantOwnerAtNewOrganizationAsync(owner, ownerId);

        var created = await owner.PostAsJsonAsync("/api/v1/service-accounts", new { name = $"retired-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var accountId = (await created.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        await GrantRoleAsync(accountId, Role.Developer, organizationId);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/v1/service-accounts/{accountId}/tokens", null)).StatusCode);

        var deniedResponse = await stranger.DeleteAsync($"/api/v1/service-accounts/{accountId}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var deleteResponse = await owner.DeleteAsync($"/api/v1/service-accounts/{accountId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var missingResponse = await owner.DeleteAsync($"/api/v1/service-accounts/{accountId}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        Assert.False(await db.ServiceAccounts.AnyAsync(a => a.Id == accountId));
        Assert.False(await db.ServiceAccountTokens.AnyAsync(t => t.ServiceAccountId == accountId));
        var assignment = await db.RoleAssignments.SingleAsync(r => r.IdentityId == accountId);
        Assert.NotNull(assignment.RevokedAt);
        Assert.True(await db.AuditLogs.AnyAsync(l => l.Action == "SERVICE_ACCOUNT_DELETED" && l.ResourceId == accountId));
    }

    internal async Task<Guid> GrantOwnerAtNewOrganizationAsync(HttpClient owner, Guid ownerId)
    {
        var orgResponse = await owner.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, organization!.Id);
        return organization.Id;
    }

    private async Task GrantRoleAsync(Guid identityId, Role role, Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        db.RoleAssignments.Add(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            Role = role,
            ScopeType = RoleScopeType.Organization,
            ScopeId = organizationId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    internal async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync()
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

    internal sealed record IdResponse(Guid Id);

    internal async Task<Guid> TokenIdForAsync(Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        return await db.ServiceAccountTokens.Where(t => t.ServiceAccountId == accountId).Select(t => t.Id).SingleAsync();
    }

    private sealed record UserDto(Guid Id, string Email, bool MfaEnabled, bool IsActive, DateTimeOffset CreatedAt);
}
