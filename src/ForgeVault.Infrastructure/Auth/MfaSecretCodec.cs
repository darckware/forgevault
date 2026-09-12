using ForgeVault.Application.Security;
using ForgeVault.Domain.Entities;

namespace ForgeVault.Infrastructure.Auth;

// Maps EncryptedPayload (Application.Security) to/from User's four mfa_secret_* columns —
// see docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §4 and User.cs's comment for why this
// is four columns rather than one packed blob.
internal static class MfaSecretCodec
{
    public static void ApplyTo(User user, EncryptedPayload payload)
    {
        user.MfaSecretCiphertext = payload.Ciphertext;
        user.MfaSecretEncryptedDek = payload.EncryptedDek;
        user.MfaSecretNonce = payload.Nonce;
        user.MfaSecretAuthTag = payload.AuthTag;
        user.MfaSecretAlgorithm = payload.Algorithm;
    }

    public static EncryptedPayload? TryGetPayload(User user)
    {
        if (user.MfaSecretCiphertext is null
            || user.MfaSecretEncryptedDek is null
            || user.MfaSecretNonce is null
            || user.MfaSecretAuthTag is null
            || user.MfaSecretAlgorithm is null)
        {
            return null;
        }

        return new EncryptedPayload(
            user.MfaSecretCiphertext,
            user.MfaSecretEncryptedDek,
            user.MfaSecretNonce,
            user.MfaSecretAuthTag,
            user.MfaSecretAlgorithm);
    }
}
