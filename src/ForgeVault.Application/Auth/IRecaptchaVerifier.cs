namespace ForgeVault.Application.Auth;

public interface IRecaptchaVerifier
{
    Task<bool> VerifyAsync(string? token, CancellationToken ct);
}
