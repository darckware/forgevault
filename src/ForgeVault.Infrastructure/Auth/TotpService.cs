using System.Security.Cryptography;
using System.Text;
using ForgeVault.Application.Auth;

namespace ForgeVault.Infrastructure.Auth;

public sealed class TotpService : ITotpService
{
    private const int SecretSizeBytes = 20; // 160-bit, the RFC 6238 default for HMAC-SHA1
    private const int StepSeconds = 30;
    private const int Digits = 6;
    private const int DriftSteps = 1; // tolerate ±30s of clock skew

    public byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(SecretSizeBytes);

    public string ComputeCode(byte[] secret, DateTimeOffset timestamp) =>
        ComputeCodeForCounter(secret, GetCounter(timestamp));

    public bool ValidateCode(byte[] secret, string code, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != Digits)
        {
            return false;
        }

        var counter = GetCounter(timestamp);
        var codeBytes = Encoding.ASCII.GetBytes(code);

        for (var drift = -DriftSteps; drift <= DriftSteps; drift++)
        {
            var candidate = Encoding.ASCII.GetBytes(ComputeCodeForCounter(secret, counter + drift));

            // Constant-time comparison — codes are always the same length, but the digits
            // themselves shouldn't be guessable one at a time via timing.
            if (CryptographicOperations.FixedTimeEquals(candidate, codeBytes))
            {
                return true;
            }
        }

        return false;
    }

    private static long GetCounter(DateTimeOffset timestamp) => timestamp.ToUnixTimeSeconds() / StepSeconds;

    private static string ComputeCodeForCounter(byte[] secret, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        var code = binaryCode % (int)Math.Pow(10, Digits);
        return code.ToString().PadLeft(Digits, '0');
    }
}
