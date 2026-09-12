using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Api.IntegrationTests;

// docs/architecture/IMPLEMENTATION_READINESS.md, milestone M6 "done when": rotating a
// secret produces version N+1 while N remains readable via /versions; reading an expired
// secret is denied and audited as a denial.
public sealed class SecretRotationAndExpirationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Rotate_CreatesNewVersion_OldVersionStaysReadable_AuditedAsRotate()
    {
        var (client, secretId) = await CreateSecretUnderFreshHierarchyAsync("sk-before-rotation");

        var rotateResponse = await client.PostAsJsonAsync($"/api/v1/secrets/{secretId}/rotate", new { value = "sk-after-rotation" });
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<SecretMetadataResponse>();
        Assert.Equal(2, rotated!.CurrentVersion);

        var versionsResponse = await client.GetAsync($"/api/v1/secrets/{secretId}/versions");
        var versions = await versionsResponse.Content.ReadFromJsonAsync<List<SecretVersionMetadataResponse>>();
        Assert.Equal(2, versions!.Count);
        Assert.Contains(versions, v => v.Version == 1);
        Assert.Contains(versions, v => v.Version == 2);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var rotateLogs = await db.AuditLogs
            .Where(a => a.ResourceId == secretId && a.Action == "SECRET_ROTATE")
            .ToListAsync();
        Assert.Single(rotateLogs);
    }

    [Fact]
    public async Task Reveal_ExpiredSecret_IsDeniedAndAuditedAsFailedAccess()
    {
        var (client, secretId) = await CreateSecretUnderFreshHierarchyAsync(
            "sk-will-expire", expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        var revealResponse = await client.GetAsync($"/api/v1/secrets/{secretId}/value?mode=REVEAL");
        Assert.Equal(HttpStatusCode.Conflict, revealResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var deniedLogs = await db.AuditLogs
            .Where(a => a.ResourceId == secretId && a.Action == "FAILED_ACCESS")
            .ToListAsync();
        Assert.Single(deniedLogs);
        Assert.Contains("secret_not_active", deniedLogs[0].Metadata, StringComparison.Ordinal);
    }

    private async Task<(HttpClient Client, Guid SecretId)> CreateSecretUnderFreshHierarchyAsync(
        string value, DateTimeOffset? expiresAt = null)
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

        var orgResponse = await client.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
            db.RoleAssignments.Add(new RoleAssignment
            {
                Id = Guid.NewGuid(),
                IdentityId = userId,
                Role = Role.Owner,
                ScopeType = RoleScopeType.Organization,
                ScopeId = organization!.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var projectResponse = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization!.Id}/projects", new { name = "forgerouter", slug = "forgerouter" });
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
            expiresAt,
        });
        var secret = await createSecretResponse.Content.ReadFromJsonAsync<IdResponse>();

        return (client, secret!.Id);
    }

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed record IdResponse(Guid Id);

    private sealed record SecretMetadataResponse(Guid Id, Guid EnvironmentId, string Name, int CurrentVersion);

    private sealed record SecretVersionMetadataResponse(Guid Id, int Version, string Algorithm, Guid CreatedBy, DateTimeOffset CreatedAt);
}
