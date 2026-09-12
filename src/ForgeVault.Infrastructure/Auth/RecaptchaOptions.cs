namespace ForgeVault.Infrastructure.Auth;

public sealed class RecaptchaOptions
{
    // Google reCAPTCHA v2 (login screen). Empty = feature off (fail-open), same convention
    // as the sibling ForgeHub/Darckware projects' own recaptcha.py — login never breaks by
    // omission; once a real secret key is provisioned, a missing/invalid token starts being
    // rejected.
    public string SecretKey { get; set; } = string.Empty;
}
