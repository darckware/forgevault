using System.Text;

namespace ForgeVault.Infrastructure.Auth;

// RFC 4648 Base32, unpadded — the encoding authenticator apps expect for a TOTP secret in
// an otpauth:// provisioning URI. .NET has no built-in Base32; this is encode-only since
// ForgeVault never needs to decode a secret back out of this format (the raw bytes are
// what get envelope-encrypted and stored).
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        var bitBuffer = 0;
        var bitCount = 0;

        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;

            while (bitCount >= 5)
            {
                bitCount -= 5;
                result.Append(Alphabet[(bitBuffer >> bitCount) & 0x1F]);
            }
        }

        if (bitCount > 0)
        {
            result.Append(Alphabet[(bitBuffer << (5 - bitCount)) & 0x1F]);
        }

        return result.ToString();
    }
}
