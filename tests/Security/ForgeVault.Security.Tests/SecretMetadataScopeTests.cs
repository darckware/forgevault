using System.Net;
using System.Net.Http.Json;

namespace ForgeVault.Security.Tests;

public sealed class SecretMetadataScopeTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task UnrelatedIdentityCannotEnumerateSecretMetadata()
    {
        var helper = new ServiceAccountDeleteTests(factory);
        var (owner, ownerId) = await helper.CreateAuthenticatedClientAsync();
        var (unrelated, _) = await helper.CreateAuthenticatedClientAsync();
        var organizationId = await helper.GrantOwnerAtNewOrganizationAsync(owner, ownerId);

        var projectResponse = await owner.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/projects",
            new { name = "private project", slug = $"private-{Guid.NewGuid():N}" });
        var projectId = (await projectResponse.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var environmentResponse = await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/environments",
            new { name = "Production", slug = "production" });
        var environmentId = (await environmentResponse.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var secretResponse = await owner.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId, name = "PRIVATE_TEST_METADATA", type = "GenericSecret", value = "test-only-value",
        });
        var secretId = (await secretResponse.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var ownerList = await owner.GetAsync("/api/v1/secrets");
        Assert.Contains(secretId.ToString(), await ownerList.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var list = await unrelated.GetAsync("/api/v1/secrets");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain(secretId.ToString(), await list.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.GetAsync($"/api/v1/secrets/{secretId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.GetAsync($"/api/v1/secrets/{secretId}/versions")).StatusCode);
    }

    [Fact]
    public async Task SecretGrantAllowsOnlyTheGrantedSecretsMetadata()
    {
        var helper = new ServiceAccountDeleteTests(factory);
        var (owner, ownerId) = await helper.CreateAuthenticatedClientAsync();
        var (grantee, granteeId) = await helper.CreateAuthenticatedClientAsync();
        var organizationId = await helper.GrantOwnerAtNewOrganizationAsync(owner, ownerId);

        var project = await owner.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/projects",
            new { name = "private project", slug = $"private-{Guid.NewGuid():N}" });
        var projectId = (await project.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        var environment = await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/environments",
            new { name = "Production", slug = "production" });
        var environmentId = (await environment.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        async Task<Guid> CreateSecretAsync(string name)
        {
            var response = await owner.PostAsJsonAsync("/api/v1/secrets", new
            {
                environmentId, name, type = "GenericSecret", value = "test-only-value",
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
        }

        var grantedId = await CreateSecretAsync("GRANTED_METADATA");
        var withheldId = await CreateSecretAsync("WITHHELD_METADATA");
        var grant = await owner.PostAsJsonAsync($"/api/v1/secrets/{grantedId}/access-grants",
            new { identityId = granteeId });
        Assert.Equal(HttpStatusCode.Created, grant.StatusCode);

        var list = await grantee.GetAsync($"/api/v1/secrets?environmentId={environmentId}");
        var body = await list.Content.ReadAsStringAsync();
        Assert.Contains(grantedId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(withheldId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/v1/secrets/{grantedId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await grantee.GetAsync($"/api/v1/secrets/{grantedId}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await grantee.GetAsync($"/api/v1/secrets/{withheldId}")).StatusCode);
    }

    private sealed record IdResponse(Guid Id);
}
