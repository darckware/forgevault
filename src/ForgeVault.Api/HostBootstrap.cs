using System.Text.Json;
using ForgeVault.Api.Auditing;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Auth;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Environment = ForgeVault.Domain.Entities.Environment;

namespace ForgeVault.Api;

// Host-local provisioning command (2026-10-16, Marcelo: ForgeHub's Remote Access needs a
// project in the vault and an identity to write machine passwords/keys there, and only an
// Organization admin could create them through the API).
//
//   docker exec forgevault-api-1 dotnet run --no-launch-profile -- host-bootstrap \
//       --project-slug infra --project-name "Infraestrutura Darckware" \
//       --environment Production --service-account service:forgehub-remote-access
//
// Why this is not a backdoor: it only runs inside the API container (no HTTP route), and
// whoever can `docker exec` there already holds the Master Key file and the database
// credentials -- the command grants nothing that access doesn't already imply. It is
// deliberately narrow: it creates (idempotently) Organization-owned Project + Environment +
// ServiceAccount, grants that account ONE role at Environment scope from a short allow-list
// (never Owner/Admin/SecurityAdmin/ProjectAdmin), and issues a token printed once on stdout
// (so the host can encrypt it without it touching disk). It never reads or writes a secret
// value: secrets are created afterwards through the normal API, with the service account's
// token, through the normal envelope encryption and audit path. Every change is audited with
// ActorType=Machine and metadata {"via":"host-bootstrap"}.
internal static class HostBootstrap
{
    public const string Command = "host-bootstrap";

    private static readonly Role[] AllowedRoles = [Role.ServiceAccount, Role.Agent, Role.Developer];

    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        var options = ParseArgs(args);
        if (options is null)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var requestId = $"host-bootstrap-{Guid.NewGuid():N}";
        var metadata = JsonSerializer.Serialize(new { via = "host-bootstrap", host_user = System.Environment.UserName });
        var actor = Guid.Empty;
        var now = DateTimeOffset.UtcNow;
        void Audit(string action, string type, Guid id) =>
            db.AuditLogs.Add(AuditLogFactory.Create(actor, AuditActorType.Machine, action, type, id, requestId, metadata));

        var orgs = await db.Organizations.OrderBy(o => o.Slug).ToListAsync();
        if (options.OrgSlug is not null)
        {
            orgs = orgs.Where(o => o.Slug == options.OrgSlug).ToList();
        }
        if (orgs.Count != 1)
        {
            Console.Error.WriteLine($"host-bootstrap: choose --org (organizations: {string.Join(", ", orgs.Select(o => o.Slug))})");
            return 2;
        }
        var org = orgs[0];

        var project = await db.Projects.SingleOrDefaultAsync(p => p.OrganizationId == org.Id && p.Slug == options.ProjectSlug);
        if (project is null)
        {
            project = new Project
            {
                Id = Guid.NewGuid(),
                OrganizationId = org.Id,
                Name = options.ProjectName,
                Slug = options.ProjectSlug,
                Description = options.ProjectDescription,
                Status = ProjectStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Projects.Add(project);
            Audit("PROJECT_CREATE", "project", project.Id);
        }

        var environment = await db.Environments.SingleOrDefaultAsync(e => e.ProjectId == project.Id && e.Name == options.Environment);
        if (environment is null)
        {
            environment = new Environment
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = options.Environment,
                Slug = options.Environment.ToString().ToLowerInvariant(),
                CreatedAt = now,
            };
            db.Environments.Add(environment);
            Audit("ENVIRONMENT_CREATE", "environment", environment.Id);
        }

        var account = await db.ServiceAccounts.SingleOrDefaultAsync(s => s.Name == options.ServiceAccount);
        if (account is null)
        {
            account = new ServiceAccount { Id = Guid.NewGuid(), Name = options.ServiceAccount, IsActive = true, CreatedAt = now };
            db.ServiceAccounts.Add(account);
            Audit("SERVICE_ACCOUNT_CREATE", "service_account", account.Id);
        }

        var hasRole = await db.RoleAssignments.AnyAsync(r =>
            r.IdentityId == account.Id && r.Role == options.Role && r.ScopeType == RoleScopeType.Environment
            && r.ScopeId == environment.Id && r.RevokedAt == null);
        if (!hasRole)
        {
            var assignment = new RoleAssignment
            {
                Id = Guid.NewGuid(),
                IdentityId = account.Id,
                Role = options.Role,
                ScopeType = RoleScopeType.Environment,
                ScopeId = environment.Id,
                CreatedAt = now,
            };
            db.RoleAssignments.Add(assignment);
            Audit("ACCESS_GRANTED", "role_assignment", assignment.Id);
        }

        string? rawToken = null;
        if (options.IssueToken)
        {
            rawToken = ServiceAccountTokenFactory.GenerateRawToken();
            db.ServiceAccountTokens.Add(new ServiceAccountToken
            {
                Id = Guid.NewGuid(),
                ServiceAccountId = account.Id,
                TokenHash = ServiceAccountTokenFactory.Hash(rawToken),
                TokenPrefix = ServiceAccountTokenFactory.ToDisplayPrefix(rawToken),
                IssuedAt = now,
            });
            Audit("TOKEN_ISSUED", "service_account", account.Id);
        }

        await db.SaveChangesAsync();

        // Ids on stderr (human-readable), the token alone on stdout so it can be piped
        // straight into `systemd-creds encrypt` without ever being written to a file.
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            organizationId = org.Id,
            projectId = project.Id,
            environmentId = environment.Id,
            serviceAccountId = account.Id,
            role = options.Role.ToString(),
            tokenIssued = rawToken is not null,
        }));
        if (rawToken is not null)
        {
            Console.Out.Write(rawToken);
        }
        return 0;
    }

    private const string Usage =
        "usage: host-bootstrap --project-slug <slug> --project-name <name> --environment <Development|Staging|Production|Shared>\n" +
        "                      --service-account <name> [--role ServiceAccount|Agent|Developer] [--org <slug>]\n" +
        "                      [--project-description <text>] [--issue-token]";

    internal sealed record Options(
        string ProjectSlug, string ProjectName, string? ProjectDescription, EnvironmentKind Environment,
        string ServiceAccount, Role Role, string? OrgSlug, bool IssueToken);

    internal static Options? ParseArgs(string[] args)
    {
        var values = new Dictionary<string, string>();
        var issueToken = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--issue-token")
            {
                issueToken = true;
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                values[args[i][2..]] = args[++i];
            }
            else
            {
                return null;
            }
        }

        if (!values.TryGetValue("project-slug", out var slug) || !values.TryGetValue("project-name", out var name)
            || !values.TryGetValue("service-account", out var account)
            || !values.TryGetValue("environment", out var envText)
            || !Enum.TryParse<EnvironmentKind>(envText, ignoreCase: false, out var env))
        {
            return null;
        }
        var role = Role.ServiceAccount;
        if (values.TryGetValue("role", out var roleText)
            && (!Enum.TryParse(roleText, ignoreCase: false, out role) || !AllowedRoles.Contains(role)))
        {
            Console.Error.WriteLine($"host-bootstrap: role must be one of {string.Join(", ", AllowedRoles)}");
            return null;
        }
        if (slug.Length is < 2 or > 60 || name.Length is < 2 or > 200 || account.Length is < 3 or > 200)
        {
            return null;
        }
        return new Options(slug, name, values.GetValueOrDefault("project-description"), env, account, role,
            values.GetValueOrDefault("org"), issueToken);
    }
}
