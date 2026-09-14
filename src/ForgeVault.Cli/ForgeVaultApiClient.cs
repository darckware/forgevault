using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ForgeVault.Cli;

// Thin REST client over the contract in docs/architecture/INTEGRATION_CONTRACT_MVP.md —
// same endpoints ForgeHub/ForgeRouter already call, just from a terminal instead of another
// service. No retry/backoff (module 08 §10 calls for it on network errors; skipped here to
// keep the MVP CLI small — a known simplification, not a design decision).
internal sealed class ForgeVaultApiClient(string baseUrl, string? bearerToken)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new() { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };

    public async Task<LoginResponse> LoginAsync(string email, string password, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
            "api/v1/auth/login", new { email, password, mfaCode = (string?)null }, JsonOptions, ct);
        return await ReadOrThrowAsync<LoginResponse>(response, ct);
    }

    public async Task<MeResponse> WhoAmIAsync(CancellationToken ct)
    {
        var response = await AuthorizedGetAsync("api/v1/auth/me", ct);
        return await ReadOrThrowAsync<MeResponse>(response, ct);
    }

    public async Task<List<SecretSummary>> ListSecretsAsync(Guid environmentId, CancellationToken ct)
    {
        var response = await AuthorizedGetAsync($"api/v1/secrets?environmentId={environmentId}", ct);
        return await ReadOrThrowAsync<List<SecretSummary>>(response, ct);
    }

    public async Task<string> RevealValueAsync(Guid secretId, CancellationToken ct)
    {
        var response = await AuthorizedGetAsync($"api/v1/secrets/{secretId}/value?mode=REVEAL", ct);
        var envelope = await ReadOrThrowAsync<CredentialEnvelope>(response, ct);
        return envelope.Credentials?.Value
            ?? throw new ForgeVaultCliException($"secret {secretId} has no revealable value (unexpected access mode response)");
    }

    private Task<HttpResponseMessage> AuthorizedGetAsync(string path, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return _http.SendAsync(request, ct);
    }

    private static async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new ForgeVaultCliException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new ForgeVaultCliException("empty response body");
    }
}

internal sealed class ForgeVaultCliException(string message) : Exception(message);

internal sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

internal sealed record MeResponse(string Id, string Email, bool MfaEnabled);

internal sealed record SecretSummary(
    string Id, string EnvironmentId, string Name, string Type, string? Provider, string? Description,
    string Status, int CurrentVersion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ExpiresAt);

internal sealed record CredentialEnvelope(
    string RequestId, string Identity, string Resource, string AccessMode,
    DateTimeOffset? ExpiresAt, CredentialValue? Credentials);

internal sealed record CredentialValue(string Value);
