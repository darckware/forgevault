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

// docs/architecture/IMPLEMENTATION_READINESS.md, milestone M5 "done when": authorized role
// gets 200 + the decrypted value; unauthorized role gets 403; both requests produce exactly
// one AuditLog row each; no audit row or application log line ever contains the raw secret
// value (mirrors docs/ForgeVault.md §21's "wrong" example as a test oracle).
public sealed class RbacRevealAuditTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task Reveal_AuthorizedGets200_UnauthorizedGets403_BothAudited_NoLeakAnywhere()
    {
        const string secretValue = "sk-proj-do-not-leak-2ce9f1";

        var (ownerClient, ownerId) = await CreateAuthenticatedClientAsync();
        var (strangerClient, _) = await CreateAuthenticatedClientAsync();

        var orgResponse = await ownerClient.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization!.Id);

        var projectResponse = await ownerClient.PostAsJsonAsync(
            $"/api/v1/organizations/{organization.Id}/projects", new { name = "forgerouter", slug = "forgerouter" });
        var project = await projectResponse.Content.ReadFromJsonAsync<IdResponse>();

        var environmentResponse = await ownerClient.PostAsJsonAsync(
            $"/api/v1/projects/{project!.Id}/environments", new { name = "Production", slug = "production" });
        var environment = await environmentResponse.Content.ReadFromJsonAsync<IdResponse>();

        var createSecretResponse = await ownerClient.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment!.Id,
            name = "OPENAI_API_KEY",
            type = "ApiKey",
            provider = "openai",
            description = (string?)null,
            value = secretValue,
            expiresAt = (DateTimeOffset?)null,
        });
        var secret = await createSecretResponse.Content.ReadFromJsonAsync<IdResponse>();

        // Unauthorized identity: authenticated, but holds no RoleAssignment anywhere near this secret.
        var deniedResponse = await strangerClient.GetAsync($"/api/v1/secrets/{secret!.Id}/value?mode=REVEAL");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        // Authorized identity (Owner at the Organization scope, inherited down to the secret).
        var allowedResponse = await ownerClient.GetAsync($"/api/v1/secrets/{secret.Id}/value?mode=REVEAL");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        var envelope = await allowedResponse.Content.ReadFromJsonAsync<CredentialEnvelopeDto>();
        Assert.Equal("REVEAL", envelope!.AccessMode);
        Assert.Equal(secretValue, envelope.Credentials.GetProperty("value").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();

            var revealLogs = await db.AuditLogs
                .Where(a => a.ResourceId == secret.Id && a.Action == "SECRET_REVEAL")
                .ToListAsync();
            Assert.Single(revealLogs);

            var deniedLogs = await db.AuditLogs
                .Where(a => a.ResourceId == secret.Id && a.Action == "FAILED_ACCESS")
                .ToListAsync();
            Assert.Single(deniedLogs);

            // No audit row for this secret — regardless of action — ever contains the raw
            // value, in any column (docs/ForgeVault.md §21's "wrong" example).
            var allLogsForSecret = await db.AuditLogs.Where(a => a.ResourceId == secret.Id).ToListAsync();
            foreach (var log in allLogsForSecret)
            {
                Assert.DoesNotContain(secretValue, log.ActorId, StringComparison.Ordinal);
                Assert.DoesNotContain(secretValue, log.Action, StringComparison.Ordinal);
                Assert.DoesNotContain(secretValue, log.Metadata, StringComparison.Ordinal);
                Assert.DoesNotContain(secretValue, log.RequestId, StringComparison.Ordinal);
            }
        }

        // Nothing the app logged during this whole flow (creation + both reveal attempts)
        // contains the plaintext value.
        var snapshot = factory.CapturedLogs.ToArray();
        Assert.NotEmpty(snapshot);
        foreach (var message in snapshot)
        {
            Assert.DoesNotContain(secretValue, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Reveal_IdorAcrossOrganizations_IsDenied()
    {
        var (ownerA, ownerAId) = await CreateAuthenticatedClientAsync();
        var (ownerB, ownerBId) = await CreateAuthenticatedClientAsync();

        var secretIdOwnedByA = await CreateSecretUnderFreshHierarchyAsync(ownerA, ownerAId, "sk-org-a-secret");

        // Owner B is a legitimate Owner — but of a *different* organization entirely. Being
        // a real, role-bearing identity elsewhere must not grant access to org A's secret.
        var orgBResponse = await ownerB.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"org-b-{Guid.NewGuid():N}",
            slug = $"org-b-{Guid.NewGuid():N}",
        });
        var organizationB = await orgBResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerBId, Role.Owner, RoleScopeType.Organization, organizationB!.Id);

        var response = await ownerB.GetAsync($"/api/v1/secrets/{secretIdOwnedByA}/value?mode=REVEAL");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyRole_DeniesBothSecretWrite_AndReveal()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (readOnlyClient, readOnlyId) = await CreateAuthenticatedClientAsync();

        var orgResponse = await owner.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization!.Id);

        // ReadOnly *is* assigned here — unlike the IDOR/no-role cases above, this proves the
        // permission matrix itself denies write/reveal for this specific role, not merely
        // "no assignment found" (docs/modules/04_AUTHORIZATION_AND_POLICY.md §4 invariant 3).
        await GrantRoleAsync(readOnlyId, Role.ReadOnly, RoleScopeType.Organization, organization.Id);

        var projectResponse = await owner.PostAsJsonAsync(
            $"/api/v1/organizations/{organization.Id}/projects", new { name = "forgerouter", slug = "forgerouter" });
        var project = await projectResponse.Content.ReadFromJsonAsync<IdResponse>();

        var environmentResponse = await owner.PostAsJsonAsync(
            $"/api/v1/projects/{project!.Id}/environments", new { name = "Production", slug = "production" });
        var environment = await environmentResponse.Content.ReadFromJsonAsync<IdResponse>();

        var createAttempt = await readOnlyClient.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment!.Id,
            name = "SHOULD_NOT_BE_CREATED",
            type = "GenericSecret",
            provider = (string?)null,
            description = (string?)null,
            value = "irrelevant",
            expiresAt = (DateTimeOffset?)null,
        });
        Assert.Equal(HttpStatusCode.Forbidden, createAttempt.StatusCode);

        // Owner creates the secret so there is something to attempt to reveal.
        var createResponse = await owner.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment.Id,
            name = "OPENAI_API_KEY",
            type = "ApiKey",
            provider = "openai",
            description = (string?)null,
            value = "sk-proj-readonly-cannot-see-this",
            expiresAt = (DateTimeOffset?)null,
        });
        var secret = await createResponse.Content.ReadFromJsonAsync<IdResponse>();

        var revealAttempt = await readOnlyClient.GetAsync($"/api/v1/secrets/{secret!.Id}/value?mode=REVEAL");
        Assert.Equal(HttpStatusCode.Forbidden, revealAttempt.StatusCode);
    }

    private async Task<Guid> CreateSecretUnderFreshHierarchyAsync(HttpClient client, Guid ownerId, string value)
    {
        var orgResponse = await client.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization!.Id);

        var projectResponse = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization.Id}/projects", new { name = "forgerouter", slug = "forgerouter" });
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
        return secret!.Id;
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

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed record IdResponse(Guid Id);

    private sealed record CredentialEnvelopeDto(string AccessMode, JsonElement Credentials);
}
