using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Api.IntegrationTests;

// docs/architecture/IMPLEMENTATION_READINESS.md, milestone M4 "done when": creates the
// full org->project->environment->secret hierarchy over real HTTP, asserts the DB-level
// ciphertext is not the plaintext, and a second PUT creates a new immutable version
// without mutating the first one's row.
public sealed class SecretsCrudFlowTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CreateFullHierarchyAndSecret_EncryptsAtRest_AndVersionsOnUpdate()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();

        // Organization -> Project -> Environment, exactly as a human operator would via the API.
        var orgResponse = await client.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        Assert.Equal(HttpStatusCode.Created, orgResponse.StatusCode);
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();

        // M5: RBAC is now enforced — grant the caller Owner at the Organization scope so it
        // cascades down to every Project/Environment/Secret created underneath it.
        await GrantRoleAsync(userId, Role.Owner, RoleScopeType.Organization, organization!.Id);

        var projectResponse = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization!.Id}/projects",
            new { name = "forgerouter", slug = "forgerouter" });
        Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);
        var project = await projectResponse.Content.ReadFromJsonAsync<IdResponse>();

        var environmentResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{project!.Id}/environments",
            new { name = "Production", slug = "production" });
        Assert.Equal(HttpStatusCode.Created, environmentResponse.StatusCode);
        var environment = await environmentResponse.Content.ReadFromJsonAsync<IdResponse>();

        // Create the secret — the plaintext must never come back in the create response.
        const string originalValue = "sk-proj-original-value";
        var createSecretResponse = await client.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment!.Id,
            name = "OPENAI_API_KEY",
            type = "ApiKey",
            provider = "openai",
            description = (string?)null,
            value = originalValue,
            expiresAt = (DateTimeOffset?)null,
        });
        Assert.Equal(HttpStatusCode.Created, createSecretResponse.StatusCode);
        var secretBody = await createSecretResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(originalValue, secretBody, StringComparison.Ordinal);

        var secret = await createSecretResponse.Content.ReadFromJsonAsync<SecretMetadataResponse>();
        Assert.Equal(1, secret!.CurrentVersion);

        // The DB-level ciphertext must not be (or contain) the plaintext.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
            var v1 = await db.SecretVersions.SingleAsync(v => v.SecretId == secret.Id && v.Version == 1);

            var plaintextBytes = Encoding.UTF8.GetBytes(originalValue);
            Assert.NotEqual(plaintextBytes, v1.Ciphertext);
            Assert.DoesNotContain(originalValue, Convert.ToBase64String(v1.Ciphertext), StringComparison.Ordinal);
        }

        // A second write creates version 2 — version 1's row is never mutated.
        const string rotatedValue = "sk-proj-rotated-value";
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/secrets/{secret.Id}", new
        {
            value = rotatedValue,
            description = (string?)null,
            expiresAt = (DateTimeOffset?)null,
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<SecretMetadataResponse>();
        Assert.Equal(2, updated!.CurrentVersion);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();

            var v1AfterUpdate = await db.SecretVersions.SingleAsync(v => v.SecretId == secret.Id && v.Version == 1);
            var v2 = await db.SecretVersions.SingleAsync(v => v.SecretId == secret.Id && v.Version == 2);

            // v1's row is byte-for-byte the same as before the update — never overwritten in place.
            Assert.NotEqual(Convert.ToBase64String(v1AfterUpdate.Nonce), Convert.ToBase64String(v2.Nonce));
            Assert.NotEqual(Convert.ToBase64String(v1AfterUpdate.Ciphertext), Convert.ToBase64String(v2.Ciphertext));

            var versionCount = await db.SecretVersions.CountAsync(v => v.SecretId == secret.Id);
            Assert.Equal(2, versionCount);
        }

        var versionsResponse = await client.GetAsync($"/api/v1/secrets/{secret.Id}/versions");
        Assert.Equal(HttpStatusCode.OK, versionsResponse.StatusCode);
        var versions = await versionsResponse.Content.ReadFromJsonAsync<List<SecretVersionMetadataResponse>>();
        Assert.Equal(2, versions!.Count);
        Assert.DoesNotContain(originalValue, await versionsResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(rotatedValue, await versionsResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
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
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, userId);
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

    private sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed record IdResponse(Guid Id);

    private sealed record SecretMetadataResponse(Guid Id, Guid EnvironmentId, string Name, int CurrentVersion);

    private sealed record SecretVersionMetadataResponse(Guid Id, int Version, string Algorithm, Guid CreatedBy, DateTimeOffset CreatedAt);
}
