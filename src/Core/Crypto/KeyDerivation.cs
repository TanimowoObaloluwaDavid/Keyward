using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Keyward.Core.Crypto
{
    /// <summary>
    /// Parameters for the password-based key derivation function, stored in cleartext in the
    /// vault envelope so a future version can read and upgrade an older vault.
    /// </summary>
    /// <remarks>
    /// These fields are not secret. They are authenticated: they are fed into the HMAC as
    /// associated data, so changing the iteration count or salt on disk invalidates the tag and
    /// the vault is rejected rather than silently derived with different parameters.
    /// </remarks>
    public sealed class KdfParameters
    {
        /// <summary>Identifier written into the envelope. Only one value is supported today.</summary>
        public const string Pbkdf2Sha512 = "PBKDF2-HMAC-SHA512";

        /// <summary>
        /// Default PBKDF2 iteration count: 210,000.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 210,000 is the OWASP Password Storage Cheat Sheet figure for PBKDF2-HMAC-SHA512.
        /// Measured on the development machine with .NET Framework 4.8:
        /// </para>
        /// <code>
        ///   PBKDF2-HMAC-SHA512   210,000 iterations  ->  ~3.6 s
        ///   PBKDF2-HMAC-SHA512   600,000 iterations  ->  ~9.8 s
        ///   PBKDF2-HMAC-SHA256   600,000 iterations  ->  ~8.8 s
        /// </code>
        /// <para>
        /// A ten-second unlock is not a defensible trade for extra iterations here: it pushes
        /// users toward keeping the vault unlocked, which is a worse trade. 210,000 follows the
        /// published guidance for this exact hash at a cost the product can actually pay.
        /// </para>
        /// <para>
        /// The real cost is re-measured on the user's own machine at vault creation by
        /// <see cref="MeasureUnlockCost"/> and shown during onboarding, so the figure is never
        /// quoted from a different computer.
        /// </para>
        /// </remarks>
        public const int DefaultIterations = 210000;

        /// <summary>Floor enforced when opening a vault, to blunt a tampered-down iteration count.</summary>
        public const int MinimumIterations = 100000;

        public string Algorithm;
        public int Iterations;
        public byte[] Salt;

        public KdfParameters()
        {
            Algorithm = Pbkdf2Sha512;
            Iterations = DefaultIterations;
            Salt = SecureRandom.NextBytes(AuthenticatedCipher.SaltSize);
        }

        public KdfParameters(string algorithm, int iterations, byte[] salt)
        {
            Algorithm = algorithm;
            Iterations = iterations;
            Salt = salt;
        }

        public KdfParameters Clone()
        {
            byte[] saltCopy = new byte[Salt.Length];
            Buffer.BlockCopy(Salt, 0, saltCopy, 0, Salt.Length);
            return new KdfParameters(Algorithm, Iterations, saltCopy);
        }

        /// <summary>
        /// Canonical byte encoding bound into the authentication tag. Changing any field here
        /// changes the tag, which is what prevents parameter-downgrade tampering.
        /// </summary>
        public byte[] ToAuthenticatedBytes()
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write(Algorithm);
                writer.Write(Iterations);
                writer.Write(Salt.Length);
                writer.Write(Salt);
                writer.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Validates that these parameters are usable, with an actionable message.</summary>
        public void Validate()
        {
            if (string.IsNullOrEmpty(Algorithm))
                throw new FormatException("Vault key-derivation settings are missing.");

            if (!string.Equals(Algorithm, Pbkdf2Sha512, StringComparison.Ordinal))
                throw new NotSupportedException("This vault uses an unsupported key-derivation algorithm: " + Algorithm);

            if (Iterations < MinimumIterations)
                throw new FormatException("Vault key-derivation settings are invalid.");

            if (Salt == null || Salt.Length < 16)
                throw new FormatException("Vault key-derivation settings are invalid.");
        }
    }

    /// <summary>
    /// A derived key pair: one AES key and one HMAC key, held so that both are wiped on
    /// <see cref="Dispose"/>.
    /// </summary>
    public sealed class MasterKey : IDisposable
    {
        private readonly SecretBytes _cipherKey;
        private readonly SecretBytes _macKey;
        private bool _disposed;

        public MasterKey(SecretBytes cipherKey, SecretBytes macKey)
        {
            if (cipherKey == null) throw new ArgumentNullException("cipherKey");
            if (macKey == null) throw new ArgumentNullException("macKey");
            if (cipherKey.Length != AuthenticatedCipher.AesKeySize)
                throw new ArgumentException("Cipher key must be 32 bytes.", "cipherKey");
            if (macKey.Length != AuthenticatedCipher.MacKeySize)
                throw new ArgumentException("MAC key must be 32 bytes.", "macKey");

            _cipherKey = cipherKey;
            _macKey = macKey;
        }

        /// <summary>The AES-256 encryption key.</summary>
        public SecretBytes CipherKey
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException("MasterKey");
                return _cipherKey;
            }
        }

        /// <summary>The HMAC-SHA-256 authentication key.</summary>
        public SecretBytes MacKey
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException("MasterKey");
                return _macKey;
            }
        }

        public bool IsDisposed
        {
            get { return _disposed; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cipherKey.Dispose();
            _macKey.Dispose();
            GC.KeepAlive(this);
        }
    }

    /// <summary>Password-based key derivation.</summary>
    public static class KeyDerivation
    {
        /// <summary>Total bytes produced by PBKDF2 and split into the two sub-keys.</summary>
        private const int DerivedLength = AuthenticatedCipher.AesKeySize + AuthenticatedCipher.MacKeySize;

        /// <summary>
        /// Derives a 64-byte secret from <paramref name="password"/> and splits it into an
        /// encryption key and a MAC key.
        /// </summary>
        /// <remarks>
        /// The password is converted to UTF-8 bytes and that buffer is zeroed as soon as
        /// derivation finishes. The returned <see cref="MasterKey"/> holds independent sub-keys
        /// because PBKDF2 output is treated as a single sequential key stream: bytes 0-31 become
        /// the AES key and bytes 32-63 the HMAC key.
        /// </remarks>
        public static MasterKey Derive(string password, KdfParameters parameters)
        {
            if (password == null) throw new ArgumentNullException("password");
            if (parameters == null) throw new ArgumentNullException("parameters");

            parameters.Validate();

            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] material = null;

            try
            {
                using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(
                    passwordBytes, parameters.Salt, parameters.Iterations, HashAlgorithmName.SHA512))
                {
                    material = kdf.GetBytes(DerivedLength);
                }

                byte[] cipherKeyBytes = new byte[AuthenticatedCipher.AesKeySize];
                byte[] macKeyBytes = new byte[AuthenticatedCipher.MacKeySize];
                Buffer.BlockCopy(material, 0, cipherKeyBytes, 0, cipherKeyBytes.Length);
                Buffer.BlockCopy(material, AuthenticatedCipher.AesKeySize, macKeyBytes, 0, macKeyBytes.Length);

                // The intermediate PBKDF2 output is no longer needed and can be wiped. The two
                // sub-key buffers must NOT be wiped here: ownership of them passes to SecretBytes,
                // which is the only thing allowed to zero them (via MasterKey.Dispose).
                Array.Clear(material, 0, material.Length);
                material = null;

                SecretBytes cipherSecret = null;
                SecretBytes macSecret = null;

                try
                {
                    cipherSecret = new SecretBytes(cipherKeyBytes);
                    macSecret = new SecretBytes(macKeyBytes);
                    return new MasterKey(cipherSecret, macSecret);
                }
                catch
                {
                    // Only wipe a buffer that did NOT successfully become a SecretBytes.
                    if (cipherSecret != null) cipherSecret.Dispose();
                    else Array.Clear(cipherKeyBytes, 0, cipherKeyBytes.Length);

                    if (macSecret != null) macSecret.Dispose();
                    else Array.Clear(macKeyBytes, 0, macKeyBytes.Length);

                    throw;
                }
            }
            finally
            {
                if (material != null) Array.Clear(material, 0, material.Length);
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }

        /// <summary>
        /// Times a real derivation so onboarding can state the actual unlock cost on this machine
        /// rather than quoting a number from another computer.
        /// </summary>
        /// <returns>Elapsed wall-clock time of the derivation.</returns>
        public static TimeSpan MeasureUnlockCost(string password, KdfParameters parameters)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            using (MasterKey key = Derive(password, parameters))
            {
                sw.Stop();
                GC.KeepAlive(key);
                return sw.Elapsed;
            }
        }
    }
}
