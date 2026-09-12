using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ForgeVault.Application.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeVault.Infrastructure.Auth;

// Google reCAPTCHA v2 server-side verification for the login screen — same convention as
// ForgeHub's app/core/recaptcha.py: fail-open when RecaptchaOptions.SecretKey is unset, so
// login never breaks by omission (docs: no key pair provisioned yet for this domain).
public sealed class RecaptchaVerifier(HttpClient httpClient, IOptions<RecaptchaOptions> options, ILogger<RecaptchaVerifier> logger)
    : IRecaptchaVerifier
{
    public async Task<bool> VerifyAsync(string? token, CancellationToken ct)
    {
        var secret = options.Value.SecretKey.Trim();
        if (secret.Length == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var response = await httpClient.PostAsync(
                "https://www.google.com/recaptcha/api/siteverify",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["secret"] = secret, ["response"] = token }),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<RecaptchaSiteVerifyResponse>(cancellationToken: ct);
            return result?.Success ?? false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Google's endpoint being unreachable must never itself lock out every login —
            // logged for visibility, treated as a failed verification (not fail-open here,
            // since a configured secret key means the operator explicitly wants enforcement).
            logger.LogWarning(ex, "reCAPTCHA verification request failed");
            return false;
        }
    }

    private sealed record RecaptchaSiteVerifyResponse([property: JsonPropertyName("success")] bool Success);
}
