using System.Security.Claims;
using ForgeVault.Api;
using ForgeVault.Application.Auth;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapPost("/login", async (LoginRequest request, IAuthService authService, IRecaptchaVerifier recaptcha, CancellationToken ct) =>
        {
            // reCAPTCHA only gates the first factor (email+password) — a request that
            // already carries an MfaCode is the LoginPage's second step, completing an
            // attempt that already passed the check once. Requiring a second, freshly-solved
            // token here (Google's tokens are single-use) was pure friction with no real
            // bot-mitigation benefit: a script that already knows valid credentials plus a
            // live TOTP code isn't the case reCAPTCHA is protecting against.
            var isMfaCompletionStep = !string.IsNullOrWhiteSpace(request.MfaCode);
            if (!isMfaCompletionStep && !await recaptcha.VerifyAsync(request.RecaptchaToken, ct))
            {
                return Results.Json(new ErrorResponse("recaptcha_failed"), statusCode: StatusCodes.Status401Unauthorized);
            }

            var outcome = await authService.LoginAsync(request.Email, request.Password, request.MfaCode, ct);
            return ToHttpResult(outcome);
        });

        group.MapPost("/refresh", async (RefreshRequest request, IAuthService authService, CancellationToken ct) =>
        {
            var outcome = await authService.RefreshAsync(request.RefreshToken, ct);
            return ToHttpResult(outcome);
        });

        // M15: now backed by a DB read instead of pure JWT claims — Username/FirstName/
        // LastName/AvatarDataUrl/IsAdmin don't exist on the token (an avatar data URL in
        // particular has no business being carried around in every request's Authorization
        // header), so the claim-only shortcut this endpoint used before no longer covers the
        // full profile.
        group.MapGet("/me", async (ForgeVaultDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var entity = await db.Users.FindAsync([user.GetUserId()], ct);
            return entity is null ? Results.NotFound() : Results.Ok(ToMeResponse(entity));
        }).RequireAuthorization();

        // Self-service profile edit — deliberately excludes Username/Email/IsAdmin/IsActive/
        // password, same boundary ForgeHub's SelfUserUpdate draws (those stay behind
        // UserManage on the admin-only PATCH /api/v1/users/{id}). No permission beyond being
        // authenticated: every identity may edit its own display name/avatar.
        group.MapPut("/me", async (UpdateMeRequest request, ForgeVaultDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (request.AvatarDataUrl is { Length: > 0 } avatar)
            {
                if (avatar.Length > 2_000_000)
                {
                    return Results.Json(new ErrorResponse("avatar_too_large"), statusCode: StatusCodes.Status400BadRequest);
                }

                if (!avatar.StartsWith("data:image/", StringComparison.Ordinal))
                {
                    return Results.Json(new ErrorResponse("avatar_must_be_a_data_url"), statusCode: StatusCodes.Status400BadRequest);
                }
            }

            var entity = await db.Users.FindAsync([user.GetUserId()], ct);
            if (entity is null)
            {
                return Results.NotFound();
            }

            if (request.FirstName is not null)
            {
                entity.FirstName = request.FirstName.Length == 0 ? null : request.FirstName;
            }

            if (request.LastName is not null)
            {
                entity.LastName = request.LastName.Length == 0 ? null : request.LastName;
            }

            if (request.AvatarDataUrl is not null)
            {
                entity.AvatarDataUrl = request.AvatarDataUrl.Length == 0 ? null : request.AvatarDataUrl;
            }

            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToMeResponse(entity));
        }).RequireAuthorization();

        group.MapPost("/change-password", async (ChangePasswordRequest request, IAuthService authService, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (request.NewPassword.Length < 8)
            {
                return Results.Json(new ErrorResponse("password_too_short"), statusCode: StatusCodes.Status400BadRequest);
            }

            var changed = await authService.ChangePasswordAsync(user.GetUserId(), request.CurrentPassword, request.NewPassword, ct);
            return changed
                ? Results.Ok(new ChangePasswordResponse(true))
                : Results.Json(new ErrorResponse("invalid_credentials"), statusCode: StatusCodes.Status401Unauthorized);
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

    private static MeResponse ToMeResponse(ForgeVault.Domain.Entities.User user) => new(
        user.Id.ToString(), user.Email, user.MfaEnabled, user.Username, user.FirstName, user.LastName, user.AvatarDataUrl, user.IsAdmin);
}

public sealed record LoginRequest(string Email, string Password, string? MfaCode, string? RecaptchaToken = null);

public sealed record RefreshRequest(string RefreshToken);

public sealed record MfaVerifyRequest(string Code);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed record ErrorResponse(string Error);

public sealed record MeResponse(
    string Id, string Email, bool MfaEnabled,
    string? Username, string? FirstName, string? LastName, string? AvatarDataUrl, bool IsAdmin);

public sealed record UpdateMeRequest(string? FirstName, string? LastName, string? AvatarDataUrl);

public sealed record MfaEnrollResponse(string Base32Secret, string OtpAuthUri);

public sealed record MfaVerifyResponse(bool Enabled);

public sealed record ChangePasswordResponse(bool Changed);
