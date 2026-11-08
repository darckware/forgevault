using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ForgeVault.Application.Authorization;
using ForgeVault.Application.Security;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeVault.Restricted.Tests;

// Real HTTP REST/MCP handlers and real PermissionChecker; a new in-memory store and
// fake encryption for each test. Never opens a socket/database or reads a master key.
public sealed class RestrictedSecretTests
{
    const string FakeValue = "fake-ssh-key-for-tests-only";

    [Theory]
    [InlineData(Role.Admin)]
    [InlineData(Role.Owner)]
    public async Task RestrictedRequiresGrantForRestAndMcpAndRender(Role role)
    {
        using var factory = new TestHost();
        var client = factory.CreateClient();
        var actor = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-Identity", actor.ToString());
        var secret = Guid.NewGuid();
        var assignment = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
            var project = new Project { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Name = "fake", Slug = "fake" };
            var env = new ForgeVault.Domain.Entities.Environment { Id = Guid.NewGuid(), ProjectId = project.Id, Slug = "fake" };
            db.Projects.Add(project); db.Environments.Add(env);
            db.Secrets.Add(new Secret { Id = secret, EnvironmentId = env.Id, Name = "FAKE_SSH_KEY", Restricted = false });
            db.SecretVersions.Add(new SecretVersion { Id = Guid.NewGuid(), SecretId = secret, Version = 1, Ciphertext = Encoding.UTF8.GetBytes(FakeValue), EncryptedDek = [], Nonce = [], AuthTag = [] });
            db.RoleAssignments.Add(new RoleAssignment { Id = Guid.NewGuid(), IdentityId = actor, Role = role, ScopeType = RoleScopeType.Organization, ScopeId = project.OrganizationId });
            var definition = new McpServerDefinition { Id = Guid.NewGuid(), OrganizationId = project.OrganizationId, Name = "fake", Command = "fake" };
            db.McpServerDefinitions.Add(definition);
            db.McpServerAssignments.Add(new McpServerAssignment { Id = assignment, IdentityId = actor, McpServerDefinitionId = definition.Id, ParamValuesJson = JsonSerializer.Serialize(new { KEY = new { secretId = secret } }) });
            await db.SaveChangesAsync();
        }
        // Existing ordinary-secret role access and assignment self-render are preserved.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/secrets/{secret}/value")).StatusCode);
        Assert.Contains(FakeValue, await Mcp(client, "credential.request", new { secretId = secret }));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/mcp-assignments/{assignment}/render")).StatusCode);
        var restriction = await client.PutAsJsonAsync($"/api/v1/secrets/{secret}/restriction", new { restricted = true });
        Assert.Equal(HttpStatusCode.OK, restriction.StatusCode);
        Assert.DoesNotContain(FakeValue, await restriction.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/secrets/{secret}/value")).StatusCode);
        Assert.Contains("forbidden", await Mcp(client, "credential.request", new { secretId = secret }));
        Assert.Contains("false", await Mcp(client, "capability.check", new { secretId = secret, permission = "SecretReadValue" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/mcp-assignments/{assignment}/render")).StatusCode);
        Assert.Contains("forbidden", await Mcp(client, "mcp.render_config", new { mcpAssignmentId = assignment }));
        foreach (var path in new[] { $"/api/v1/secrets/{secret}", $"/api/v1/secrets/{secret}/versions", "/api/v1/secrets" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(FakeValue, await response.Content.ReadAsStringAsync());
        }
        var rotated = await client.PostAsJsonAsync($"/api/v1/secrets/{secret}/rotate", new { value = FakeValue });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.DoesNotContain(FakeValue, await rotated.Content.ReadAsStringAsync());
        Assert.DoesNotContain(FakeValue, await Mcp(client, "admin.secret.rotate", new { secretId = secret, value = FakeValue }));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/secrets/{secret}/value")).StatusCode);
        // Admin can still explicitly self-grant: a documented governance limitation.
        var grant = await client.PostAsJsonAsync($"/api/v1/secrets/{secret}/access-grants", new { identityId = actor });
        var grantId = (await grant.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/secrets/{secret}/value")).StatusCode);
        Assert.Contains(FakeValue, await Mcp(client, "credential.request", new { secretId = secret }));
        Assert.Contains("true", await Mcp(client, "capability.check", new { secretId = secret, permission = "SecretReadValue" }));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/mcp-assignments/{assignment}/render")).StatusCode);
        Assert.Contains(FakeValue, await Mcp(client, "mcp.render_config", new { mcpAssignmentId = assignment }));
        await client.PostAsync($"/api/v1/secret-access-grants/{grantId}/revoke", null);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/secrets/{secret}/value")).StatusCode);
        Assert.Contains("forbidden", await Mcp(client, "credential.request", new { secretId = secret }));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/mcp-assignments/{assignment}/render")).StatusCode);
        // A consumer with no role reads through the same bridge REST envelope contract.
        var consumer = Guid.NewGuid();
        await client.PostAsJsonAsync($"/api/v1/secrets/{secret}/access-grants", new { identityId = consumer });
        client.DefaultRequestHeaders.Remove("X-Test-Identity");
        client.DefaultRequestHeaders.Add("X-Test-Identity", consumer.ToString());
        var envelope = await client.GetFromJsonAsync<JsonElement>($"/api/v1/secrets/{secret}/value?mode=REVEAL");
        Assert.Equal(FakeValue, envelope.GetProperty("credentials").GetProperty("value").GetString());
        Assert.Contains(FakeValue, await Mcp(client, "credential.request", new { secretId = secret }));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/v1/secrets/{secret}/restriction", new { restricted = false })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Identity"); client.DefaultRequestHeaders.Add("X-Test-Identity", actor.ToString());
        Assert.DoesNotContain(FakeValue, await Mcp(client, "admin.secret.set_restriction", new { secretId = secret, restricted = false }));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/secrets/{secret}/value")).StatusCode);
        using var auditScope = factory.Services.CreateScope();
        var logs = await auditScope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>().AuditLogs.ToListAsync();
        Assert.Equal(2, logs.Count(l => l.Action == "SECRET_RESTRICTION_CHANGED"));
        Assert.Contains(logs, l => l.Action == "FAILED_ACCESS" && l.ResourceId == secret);
        Assert.Contains(logs, l => l.Action == "SECRET_ACCESS_GRANTED");
        Assert.Contains(logs, l => l.Action == "SECRET_ACCESS_REVOKED");
        Assert.DoesNotContain(FakeValue, JsonSerializer.Serialize(logs));
    }

    static async Task<string> Mcp(HttpClient client, string name, object arguments)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.ParseAdd("application/json"); request.Headers.Accept.ParseAdd("text/event-stream");
        request.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments } });
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}

sealed class TestHost : WebApplicationFactory<Program>
{
    readonly string database = Guid.NewGuid().ToString();
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        services.RemoveAll<DbContextOptions<ForgeVaultDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<ForgeVaultDbContext>>();
        services.AddDbContext<ForgeVaultDbContext>(o => o.UseInMemoryDatabase(database));
        services.RemoveAll<IKeyManagementProvider>(); services.AddSingleton<IKeyManagementProvider, FakeKey>();
        services.RemoveAll<IEnvelopeEncryptionService>(); services.AddSingleton<IEnvelopeEncryptionService, FakeCrypto>();
        services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; o.DefaultForbidScheme = "Test"; })
            .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
    });
}
sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Request.Headers["X-Test-Identity"].ToString())], "Test")), "Test")));
}
sealed class FakeCrypto : IEnvelopeEncryptionService
{
    public Task<EncryptedPayload> EncryptAsync(byte[] plaintext, CancellationToken ct) => Task.FromResult(new EncryptedPayload(plaintext, [], [], [], "fake"));
    public Task<byte[]> DecryptAsync(EncryptedPayload payload, CancellationToken ct) => Task.FromResult(payload.Ciphertext);
}
sealed class FakeKey : IKeyManagementProvider
{
    public string ProviderName => "fake";
    public Task<byte[]> GetActiveMasterKeyAsync(CancellationToken ct) => Task.FromResult(new byte[32]);
    public Task<byte[]> WrapDekAsync(byte[] dek, CancellationToken ct) => Task.FromResult(dek);
    public Task<byte[]> UnwrapDekAsync(byte[] encryptedDek, CancellationToken ct) => Task.FromResult(encryptedDek);
}
