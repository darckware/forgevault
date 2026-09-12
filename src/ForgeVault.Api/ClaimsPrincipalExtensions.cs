using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ForgeVault.Domain.Entities;

namespace ForgeVault.Api;

internal static class ClaimsPrincipalExtensions
{
    // The "sub" claim is a User.Id for human logins, or a ServiceAccount.Id for service
    // account tokens (M7, docs/architecture/IMPLEMENTATION_READINESS.md) — both are Guids,
    // so callers that only need an identity id (RBAC checks, audit actor_id) work unchanged
    // regardless of which kind of identity is calling.
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    // Present only on tokens issued by ServiceAccountAuthenticationHandler — absent for
    // human JWT logins, which is what makes this distinguishable.
    public static AuditActorType GetActorType(this ClaimsPrincipal user) =>
        user.HasClaim(c => c.Type == "identity_type" && c.Value == "service")
            ? AuditActorType.Service
            : AuditActorType.Human;
}
