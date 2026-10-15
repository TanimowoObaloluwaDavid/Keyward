using System;
using System.IO;
using System.Security.Cryptography;

namespace Keyward.Core.Crypto
{
    /// <summary>The three components of an encrypt-then-MAC message.</summary>
    public sealed class SealedMessage
    {
        public byte[] Iv;
        public byte[] Ciphertext;
        public byte[] Mac;

        public SealedMessage(byte[] iv, byte[] ciphertext, byte[] mac)
        {
            Iv = iv;
            Ciphertext = ciphertext;
            Mac = mac;
        }
    }

    /// <summary>
    /// Raised when a message fails authentication. The exception deliberately carries no detail
    /// about <em>why</em> it failed, because "wrong password" and "the file was tampered with"
    /// must be indistinguishable to an attacker.
    /// </summary>
    public class IntegrityException : Exception
    {
        public IntegrityException(string message) : base(message) { }
        public IntegrityException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Authenticated encryption using AES-256-CBC with an encrypt-then-MAC HMAC-SHA-256 tag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why not AES-GCM.</b> <c>System.Security.Cryptography.AesGcm</c> only exists from
    /// .NET Core 3.0 / .NET 5 onwards and is absent from .NET Framework 4.8, which is the runtime
    /// this application targets. Rather than hand-roll GCM (which would be reckless for a
    /// security product), Keyward uses the classic encrypt-then-MAC composition with two keys
    /// derived independently from the same PBKDF2 output. Encrypt-then-MAC is a standard,
    /// well-understood AEAD-equivalent construction; the decryption step is never reached unless
    /// the MAC verifies first.
    /// </para>
    /// <para>
    /// <b>Key separation.</b> The caller must pass two distinct 32-byte keys: one for AES, one for
    /// HMAC. Reusing a single key across two primitives is never safe and is not supported by
    /// <see cref="Seal"/>/<see cref="Open"/>, which reject keys of the wrong length.
    /// </para>
    /// <para>
    /// <b>Authenticated data.</b> Version and algorithm identifiers are passed as associated data
    /// so an attacker cannot downgrade the envelope to an older, weaker format without the tag
    /// failing to verify.
    /// </para>
    /// </remarks>
    public static class AuthenticatedCipher
    {
        public const int AesKeySize = 32;
        public const int MacKeySize = 32;
        public const int IvSize = 16;
        public const int MacSize = 32;
        public const int SaltSize = 32;

        /// <summary>Human-readable cipher identifier stored in the vault envelope.</summary>
        public const string CipherName = "AES-256-CBC+HMAC-SHA256";

        /// <summary>
        /// Encrypts <paramref name="plaintext"/> and returns the IV, ciphertext and authentication tag.
        /// A fresh random IV is generated per call; it is never derived from the key or the payload.
        /// </summary>
        public static SealedMessage Seal(SecretBytes cipherKey, SecretBytes macKey, byte[] plaintext, byte[] associatedData)
        {
            if (cipherKey == null) throw new ArgumentNullException("cipherKey");
            if (macKey == null) throw new ArgumentNullException("macKey");
            if (plaintext == null) throw new ArgumentNullException("plaintext");
            if (cipherKey.Length != AesKeySize) throw new ArgumentException("Cipher key must be 32 bytes.", "cipherKey");
            if (macKey.Length != MacKeySize) throw new ArgumentException("MAC key must be 32 bytes.", "macKey");

            byte[] iv = SecureRandom.NextBytes(IvSize);

            byte[] ciphertext;
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = cipherKey.Buffer;
                aes.IV = iv;

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
                }
            }

            byte[] mac = ComputeMac(macKey, associatedData, iv, ciphertext);

            // The IV is public and not secret, but it is not sensitive either; only the keys and
            // plaintext are, and they live in SecretBytes rather than here.
            return new SealedMessage(iv, ciphertext, mac);
        }

        /// <summary>
        /// Verifies the authentication tag and, only if it is valid, decrypts the payload.
        /// </summary>
        /// <exception cref="IntegrityException">
        /// Thrown when the tag does not match. The caller cannot distinguish a wrong master
        /// password from a modified vault file, and that ambiguity is intentional.
        /// </exception>
        public static byte[] Open(SecretBytes cipherKey, SecretBytes macKey, SealedMessage message, byte[] associatedData)
        {
            if (cipherKey == null) throw new ArgumentNullException("cipherKey");
            if (macKey == null) throw new ArgumentNullException("macKey");
            if (message == null) throw new ArgumentNullException("message");
            if (message.Iv == null || message.Ciphertext == null || message.Mac == null)
                throw new IntegrityException("Vault data is malformed.");
            if (cipherKey.Length != AesKeySize) throw new ArgumentException("Cipher key must be 32 bytes.", "cipherKey");
            if (macKey.Length != MacKeySize) throw new ArgumentException("MAC key must be 32 bytes.", "macKey");
            if (message.Iv.Length != IvSize)
                throw new IntegrityException("Vault data is malformed.");
            if (message.Mac.Length != MacSize)
                throw new IntegrityException("Vault data is malformed.");

            // Verify before decrypting. Decrypting first would expose the application to padding
            // oracle behaviour and would waste work on data that is going to be rejected anyway.
            byte[] expected = ComputeMac(macKey, associatedData, message.Iv, message.Ciphertext);
            byte[] presented = new byte[message.Mac.Length];
            Buffer.BlockCopy(message.Mac, 0, presented, 0, presented.Length);

            if (!FixedTimeEquals(expected, presented))
            {
                Array.Clear(expected, 0, expected.Length);
                throw new IntegrityException("Vault data is malformed.");
            }
            Array.Clear(expected, 0, expected.Length);

            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = cipherKey.Buffer;
                    aes.IV = message.Iv;

                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    {
                        return decryptor.TransformFinalBlock(message.Ciphertext, 0, message.Ciphertext.Length);
                    }
                }
            }
            catch (CryptographicException ex)
            {
                // Reached only when the tag verified but the ciphertext will not decrypt, which
                // should be impossible. Treated as an integrity failure, never as success.
                throw new IntegrityException("Vault data is malformed.", ex);
            }
        }

        /// <summary>
        /// HMAC-SHA256 over a canonical, length-prefixed encoding of
        /// (associatedData, iv, ciphertext) so no two distinct field splits can collide.
        /// </summary>
        private static byte[] ComputeMac(SecretBytes macKey, byte[] associatedData, byte[] iv, byte[] ciphertext)
        {
            using (HMACSHA256 hmac = new HMACSHA256(macKey.Buffer))
            using (MemoryStream buffer = new MemoryStream())
            {
                WriteLengthPrefixed(buffer, associatedData);
                WriteLengthPrefixed(buffer, iv);
                WriteLengthPrefixed(buffer, ciphertext);

                byte[] canonical = buffer.ToArray();
                try
                {
                    return hmac.ComputeHash(canonical);
                }
                finally
                {
                    Array.Clear(canonical, 0, canonical.Length);
                }
            }
        }

        private static void WriteLengthPrefixed(Stream stream, byte[] value)
        {
            if (value == null)
            {
                stream.Write(BitConverter.GetBytes(0), 0, 4);
                return;
            }

            stream.Write(BitConverter.GetBytes(value.Length), 0, 4);
            stream.Write(value, 0, value.Length);
        }

        /// <summary>
        /// Compares two byte arrays without an early exit, so the time taken does not leak how
        /// many leading bytes matched.
        /// </summary>
        public static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;

            int difference = 0;
            for (int i = 0; i < a.Length; i++)
                difference |= a[i] ^ b[i];

            return difference == 0;
        }

        /// <summary>Constant-time comparison of two UTF-8 strings, for master-password checks.</summary>
        public static bool FixedTimeEqualsUtf8(string a, string b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            byte[] left = System.Text.Encoding.UTF8.GetBytes(a);
            byte[] right = System.Text.Encoding.UTF8.GetBytes(b);

            try
            {
                if (left.Length != right.Length)
                {
                    // Still walk both buffers so the cost does not depend on length mismatch.
                    int ignored = 0;
                    for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
                        ignored |= left[i] ^ right[i];
                    return false;
                }

                return FixedTimeEquals(left, right);
            }
            finally
            {
                Array.Clear(left, 0, left.Length);
                Array.Clear(right, 0, right.Length);
            }
        }
    }
}
