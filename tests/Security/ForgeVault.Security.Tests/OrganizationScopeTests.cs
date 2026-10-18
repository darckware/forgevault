using System.Net;
using System.Net.Http.Json;

namespace ForgeVault.Security.Tests;

public sealed class OrganizationScopeTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task AdminOfOneOrganizationCannotChangeOrDeleteAnother()
    {
        var helper = new ServiceAccountDeleteTests(factory);
        var (admin, adminId) = await helper.CreateAuthenticatedClientAsync();
        var (other, otherId) = await helper.CreateAuthenticatedClientAsync();
        await helper.GrantOwnerAtNewOrganizationAsync(admin, adminId);
        var otherOrganizationId = await helper.GrantOwnerAtNewOrganizationAsync(other, otherId);

        var update = await admin.PutAsJsonAsync($"/api/v1/organizations/{otherOrganizationId}", new
        {
            name = "Unauthorized change",
        });
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        var delete = await admin.DeleteAsync($"/api/v1/organizations/{otherOrganizationId}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }
}
