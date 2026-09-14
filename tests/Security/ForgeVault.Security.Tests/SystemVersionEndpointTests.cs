using System.Net;
using System.Net.Http.Json;

namespace ForgeVault.Security.Tests;

// M16 — GET /api/v1/system/version is unauthenticated by design (same convention as
// /health/live and /health/ready, and as ForgeHub's own equivalent route): build identity,
// never secret data, so no reason to gate it.
public sealed class SystemVersionEndpointTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task ReturnsBuildIdentity_WithoutAuthentication()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/system/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<VersionDto>();
        Assert.NotNull(body!.PostgresVersion);
        Assert.NotNull(body.LatestMigrationBundled);
        Assert.Equal("https://github.com/marcelodarckferreira/forgevault", body.GithubRepoUrl);
    }

    private sealed record VersionDto(
        string AppVersion, string GitSha, string? GitCommitUrl, string BuildDate,
        string? PostgresVersion, string? LatestMigrationBundled, string GithubRepoUrl);
}
