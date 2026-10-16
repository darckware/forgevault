using ForgeVault.Api;
using ForgeVault.Domain.Entities;

namespace ForgeVault.Security.Tests;

// HostBootstrap is host-local provisioning; its safety rests on a narrow argument surface.
public class HostBootstrapArgsTests
{
    private static readonly string[] Base =
    [
        "--project-slug", "infra", "--project-name", "Infraestrutura", "--environment", "Production",
        "--service-account", "forgehub-remote-access",
    ];

    [Fact]
    public void Defaults_to_the_ServiceAccount_role_without_a_token()
    {
        var options = HostBootstrap.ParseArgs(Base);
        Assert.NotNull(options);
        Assert.Equal(Role.ServiceAccount, options!.Role);
        Assert.Equal(EnvironmentKind.Production, options.Environment);
        Assert.False(options.IssueToken);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    [InlineData("SecurityAdmin")]
    [InlineData("ProjectAdmin")]
    [InlineData("NotARole")]
    public void Refuses_administrative_or_unknown_roles(string role)
    {
        Assert.Null(HostBootstrap.ParseArgs([.. Base, "--role", role]));
    }

    [Fact]
    public void Accepts_the_allowed_roles_and_the_token_flag()
    {
        var options = HostBootstrap.ParseArgs([.. Base, "--role", "Agent", "--issue-token"]);
        Assert.Equal(Role.Agent, options!.Role);
        Assert.True(options.IssueToken);
    }

    [Fact]
    public void Requires_every_mandatory_argument()
    {
        Assert.Null(HostBootstrap.ParseArgs(["--project-slug", "infra"]));
        Assert.Null(HostBootstrap.ParseArgs([.. Base[..6], "--service-account", "x"])); // too short
        Assert.Null(HostBootstrap.ParseArgs([.. Base, "stray"]));
        Assert.Null(HostBootstrap.ParseArgs(["--project-slug", "infra", "--project-name", "N", "--environment", "Prod", "--service-account", "abc"]));
    }
}
