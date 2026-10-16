using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

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
        await helper.GrantOwnerAtNewOrganizationAsync(owner, ownerId);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await stranger.PostAsJsonAsync("/api/v1/service-accounts", new { name = $"x-{Guid.NewGuid():N}" })).StatusCode);

        var created = await owner.PostAsJsonAsync("/api/v1/service-accounts", new { name = $"victim-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var accountId = (await created.Content.ReadFromJsonAsync<ServiceAccountDeleteTests.IdResponse>())!.Id;

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

    private sealed record TokenResponse(string Token);
}
