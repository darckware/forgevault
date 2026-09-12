using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ForgeVault.Infrastructure.Auth;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeVault.Api.Authentication;

public static class ServiceAccountAuthenticationDefaults
{
    public const string AuthenticationScheme = "ServiceAccount";
}

// docs/ForgeVault.md §31-32, §76 (M7). Validates a raw "fv_sa_..." bearer token against
// service_account_tokens by hash — a completely separate mechanism from JwtBearer (M3),
// which only ever validates signed JWTs. See Program.cs's "SmartAuth" policy scheme for how
// a request is routed to this handler vs. JwtBearer based on the token's prefix.
public sealed class ServiceAccountAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ForgeVaultDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var rawToken = header["Bearer ".Length..].Trim();
        var tokenHash = ServiceAccountTokenFactory.Hash(rawToken);

        var token = await db.ServiceAccountTokens
            .Include(t => t.ServiceAccount)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (token is null)
        {
            return AuthenticateResult.Fail("Unrecognized service account token.");
        }

        if (token.RevokedAt is not null || (token.ExpiresAt is not null && token.ExpiresAt <= DateTimeOffset.UtcNow))
        {
            return AuthenticateResult.Fail("Service account token is revoked or expired.");
        }

        if (token.ServiceAccount is null || !token.ServiceAccount.IsActive)
        {
            return AuthenticateResult.Fail("Service account is not active.");
        }

        token.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        Claim[] claims =
        [
            new(JwtRegisteredClaimNames.Sub, token.ServiceAccount.Id.ToString()),
            new("identity_type", "service"),
            new("service_account_name", token.ServiceAccount.Name),
        ];

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
