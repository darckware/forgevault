using Microsoft.AspNetCore.Authorization;

namespace ForgeVault.Api.Authorization;

// docs/architecture/IMPLEMENTATION_READINESS.md §4: the MFA enforcement policy hook exists
// from M3 onward so endpoints can opt in with [Authorize(Policy = "RequireMfa")] without a
// design change. TOTP enrollment/verification landed in M6 — applied so far to
// GET /secrets/{id}/value (docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §15's explicit
// "leitura de secrets críticos" example). Broader coverage (all admin actions, break-glass,
// export) needs the risk-level model from module 04 §96, not yet implemented.
public sealed class MfaRequirement : IAuthorizationRequirement;
