using System.Security.Cryptography;

namespace ForgeVault.Infrastructure.Security;

// Shared low-level AES-256-GCM helper used both by LocalFileKeyProvider (wrapping a DEK
// under the Master Key) and AesGcmEnvelopeEncryptionService (encrypting secret plaintext
// under a DEK) — kept as a single implementation so the two call sites can't silently
// drift into different nonce/tag handling.
internal static class AesGcmCodec
{
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    public static byte[] Encrypt(byte[] key, byte[] plaintext, out byte[] nonce, out byte[] tag)
    {
        nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        tag = new byte[TagSizeBytes];
        var ciphertext = new byte[plaintext.Length];

        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        return ciphertext;
    }

    // Throws CryptographicException when the tag doesn't match — GCM's authentication
    // check catches both bit-flip tampering and decryption under the wrong key, never
    // returning corrupted/garbage plaintext silently.
    public static byte[] Decrypt(byte[] key, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    // Combines nonce||tag||ciphertext into one blob — only needed where the contract
    // returns a single byte[] (IKeyManagementProvider.WrapDekAsync). EncryptedPayload keeps
    // nonce/tag/ciphertext as separate fields, matching the secret_versions columns.
    public static byte[] Pack(byte[] nonce, byte[] tag, byte[] ciphertext)
    {
        var packed = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, packed, nonce.Length + tag.Length, ciphertext.Length);
        return packed;
    }

    public static (byte[] Nonce, byte[] Tag, byte[] Ciphertext) Unpack(byte[] packed)
    {
        if (packed.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Packed payload is too short to contain a nonce and auth tag.");
        }

        var nonce = packed[..NonceSizeBytes];
        var tag = packed[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        var ciphertext = packed[(NonceSizeBytes + TagSizeBytes)..];
        return (nonce, tag, ciphertext);
    }
}
