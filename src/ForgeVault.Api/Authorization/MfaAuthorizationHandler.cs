using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ForgeVault.Api.Authorization;

// docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §15: MFA is mandatory for specific
// sensitive actions — but only once an account has actually enrolled. An account that
// never called /auth/mfa/enroll is unaffected even when this policy is applied to an
// endpoint, so turning enforcement on (M6) doesn't retroactively lock out every existing
// account created before TOTP existed.
public sealed class MfaAuthorizationHandler(IOptions<MfaEnforcementOptions> options)
    : AuthorizationHandler<MfaRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MfaRequirement requirement)
    {
        if (!options.Value.Enabled)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var accountHasMfaEnabled = context.User.HasClaim(c => c.Type == "mfa_enabled" && c.Value == "true");
        if (!accountHasMfaEnabled)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.User.HasClaim(c => c.Type == "mfa_verified" && c.Value == "true"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
