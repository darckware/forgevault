using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ForgeVault.Api;
using ForgeVault.Application.Auth;

namespace ForgeVault.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService authService, CancellationToken ct) =>
        {
            var outcome = await authService.LoginAsync(request.Email, request.Password, request.MfaCode, ct);
            return ToHttpResult(outcome);
        });

        group.MapPost("/refresh", async (RefreshRequest request, IAuthService authService, CancellationToken ct) =>
        {
            var outcome = await authService.RefreshAsync(request.RefreshToken, ct);
            return ToHttpResult(outcome);
        });

        // Placeholder authenticated endpoint (docs/architecture/IMPLEMENTATION_READINESS.md,
        // M3 "done when": an authenticated call succeeds, an unauthenticated one gets 401).
        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var id = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var email = user.FindFirstValue(JwtRegisteredClaimNames.Email);
            return Results.Ok(new MeResponse(id!, email!));
        }).RequireAuthorization();

        // docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §5 UC-05 (M6). Enrolling alone does
        // not enable MFA — the account must also complete /mfa/verify with a real code.
        group.MapPost("/mfa/enroll", async (IAuthService authService, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var enrollment = await authService.EnrollMfaAsync(user.GetUserId(), ct);
            return Results.Ok(new MfaEnrollResponse(enrollment.Base32Secret, enrollment.OtpAuthUri));
        }).RequireAuthorization();

        group.MapPost("/mfa/verify", async (MfaVerifyRequest request, IAuthService authService, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var verified = await authService.VerifyMfaAsync(user.GetUserId(), request.Code, ct);
            return verified
                ? Results.Ok(new MfaVerifyResponse(true))
                : Results.Json(new ErrorResponse("invalid_mfa_code"), statusCode: StatusCodes.Status401Unauthorized);
        }).RequireAuthorization();
    }

    // Named records (rather than anonymous objects) so the response shape is explicit and
    // consistently serialized by ASP.NET Core's default camelCase JSON policy.
    private static IResult ToHttpResult(AuthOutcome outcome) => outcome switch
    {
        AuthSuccess success => Results.Ok(new LoginResponse(success.AccessToken, success.RefreshToken, success.ExpiresAt)),
        AuthFailure failure => Results.Json(new ErrorResponse(failure.Reason), statusCode: StatusCodes.Status401Unauthorized),
        _ => Results.Problem(),
    };
}

public sealed record LoginRequest(string Email, string Password, string? MfaCode);

public sealed record RefreshRequest(string RefreshToken);

public sealed record MfaVerifyRequest(string Code);

public sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed record ErrorResponse(string Error);

public sealed record MeResponse(string Id, string Email);

public sealed record MfaEnrollResponse(string Base32Secret, string OtpAuthUri);

public sealed record MfaVerifyResponse(bool Enabled);
