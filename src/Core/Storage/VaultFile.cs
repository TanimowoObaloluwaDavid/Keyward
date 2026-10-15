using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Keyward.Core.Crypto;
using Keyward.Core.Domain;

namespace Keyward.Core.Storage
{
    /// <summary>Why a vault could not be opened. Lets the UI show a specific, honest message.</summary>
    public enum VaultOpenFailure
    {
        None = 0,
        FileMissing,
        FileUnreadable,
        Malformed,
        WrongPasswordOrTampered,
        UnsupportedVersion
    }

    /// <summary>Outcome of an attempt to open a vault.</summary>
    public sealed class VaultOpenResult
    {
        public bool Success;
        public VaultOpenFailure Failure;
        public MasterKey Key;
        public KdfParameters Parameters;
        public VaultDocument Document;

        /// <summary>A message safe to show a user. Never contains key material.</summary>
        public string Message;

        public static VaultOpenResult Ok(MasterKey key, KdfParameters p, VaultDocument doc)
        {
            return new VaultOpenResult
            {
                Success = true,
                Failure = VaultOpenFailure.None,
                Key = key,
                Parameters = p,
                Document = doc
            };
        }

        public static VaultOpenResult Fail(VaultOpenFailure failure, string message)
        {
            return new VaultOpenResult { Success = false, Failure = failure, Message = message };
        }
    }

    /// <summary>
    /// Reads and writes the encrypted vault file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file is a small JSON envelope. Everything sensitive is inside
    /// <c>cipher.ciphertext</c>; nothing outside it reveals the contents of the vault. The
    /// key-derivation parameters are stored in clear because the recipient needs them, but they
    /// are bound into the authentication tag so they cannot be tampered with.
    /// </para>
    /// <para>
    /// Shape:
    /// </para>
    /// <code>
    /// {
    ///   "format": "keyward-vault",
    ///   "version": 2,
    ///   "kdf":    { "algorithm": "...", "iterations": N, "salt": "base64" },
    ///   "cipher": { "algorithm": "...", "iv": "base64", "ciphertext": "base64", "mac": "base64" },
    ///   "updatedUtc": "..."
    /// }
    /// </code>
    /// </remarks>
    public static class VaultFile
    {
        public const string FormatMarker = "keyward-vault";
        public const int CurrentVersion = 2;

        /// <summary>
        /// Creates a brand new vault at <paramref name="path"/> and writes an empty document.
        /// </summary>
        /// <returns>The freshly derived key; the caller owns it and must dispose it.</returns>
        public static MasterKey Create(string path, string masterPassword, out KdfParameters parameters)
        {
            parameters = new KdfParameters();
            MasterKey key = KeyDerivation.Derive(masterPassword, parameters);

            VaultDocument document = new VaultDocument();
            try
            {
                Write(path, key, parameters, document);
            }
            catch
            {
                key.Dispose();
                throw;
            }

            return key;
        }

        /// <summary>
        /// Opens a vault with the supplied master password.
        /// </summary>
        /// <remarks>
        /// A wrong password and a tampered file produce the same failure. That is deliberate: an
        /// attacker who can distinguish "you typed the wrong password" from "this file has been
        /// modified" learns something useful, and there is nothing the user can do about the
        /// second case anyway.
        /// </remarks>
        public static VaultOpenResult Open(string path, string masterPassword)
        {
            if (!File.Exists(path))
                return VaultOpenResult.Fail(VaultOpenFailure.FileMissing, "No vault was found at this location.");

            byte[] raw;
            try
            {
                raw = AtomicFile.ReadAllBytes(path);
            }
            catch (FileNotFoundException)
            {
                return VaultOpenResult.Fail(VaultOpenFailure.FileMissing, "No vault was found at this location.");
            }
            catch (IOException ex)
            {
                return VaultOpenResult.Fail(VaultOpenFailure.FileUnreadable,
                    "The vault file could not be read. " + ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return VaultOpenResult.Fail(VaultOpenFailure.FileUnreadable,
                    "The vault file could not be opened. Another program may be holding a lock on it.");
            }

            JsonObject root;
            try
            {
                string text = Encoding.UTF8.GetString(raw);
                root = JsonValue.Parse(text) as JsonObject;
                if (root == null) throw new JsonParseException("Vault file is not a JSON object.", 0);
            }
            catch (JsonParseException)
            {
                return VaultOpenResult.Fail(VaultOpenFailure.Malformed,
                    "This file is not a Keyward vault. It may be corrupted or it may be a different kind of file.");
            }

            string format = root.GetString("format", string.Empty);
            if (!string.Equals(format, FormatMarker, StringComparison.Ordinal))
                return VaultOpenResult.Fail(VaultOpenFailure.Malformed,
                    "This file is not a Keyward vault.");

            int version = root.GetInt("version", 0);
            if (version <= 0)
                return VaultOpenResult.Fail(VaultOpenFailure.Malformed, "The vault file is missing its version.");
            if (version > CurrentVersion)
                return VaultOpenResult.Fail(VaultOpenFailure.UnsupportedVersion,
                    "This vault was created by a newer version of Keyward. Please update the application.");

            KdfParameters parameters;
            SealedMessage message;
            string cipherAlgorithm;

            try
            {
                JsonObject kdf = root.GetObject("kdf");
                JsonObject cipher = root.GetObject("cipher");
                if (kdf == null || cipher == null)
                    return VaultOpenResult.Fail(VaultOpenFailure.Malformed, "The vault file is missing required sections.");

                parameters = new KdfParameters(
                    kdf.GetString("algorithm", string.Empty),
                    kdf.GetInt("iterations", 0),
                    Convert.FromBase64String(kdf.GetRequiredString("salt")));

                cipherAlgorithm = cipher.GetString("algorithm", string.Empty);
                message = new SealedMessage(
                    Convert.FromBase64String(cipher.GetRequiredString("iv")),
                    Convert.FromBase64String(cipher.GetRequiredString("ciphertext")),
                    Convert.FromBase64String(cipher.GetRequiredString("mac")));
            }
            catch (FormatException)
            {
                return VaultOpenResult.Fail(VaultOpenFailure.Malformed, "The vault file is malformed.");
            }
            catch (KeyNotFoundException)
            {
                return VaultOpenResult.Fail(VaultOpenFailure.Malformed, "The vault file is malformed.");
            }

            if (!string.Equals(cipherAlgorithm, AuthenticatedCipher.CipherName, StringComparison.Ordinal))
                return VaultOpenResult.Fail(VaultOpenFailure.Malformed, "The vault uses an unsupported cipher.");

            MasterKey key;
            try
            {
                key = KeyDerivation.Derive(masterPassword, parameters);
            }
            catch (FormatException)
            {
                return VaultOpenResult.Fail(VaultOpenFailure.Malformed, "The vault's key settings are invalid.");
            }
            catch (NotSupportedException)
            {
                return VaultOpenResult.Fail(VaultOpenFailure.UnsupportedVersion,
                    "This vault uses a key-derivation method this version cannot read.");
            }

            byte[] plaintext;
            try
            {
                byte[] aad = BuildAssociatedData(version, cipherAlgorithm, parameters);
                plaintext = AuthenticatedCipher.Open(key.CipherKey, key.MacKey, message, aad);
            }
            catch (IntegrityException)
            {
                key.Dispose();
                return VaultOpenResult.Fail(VaultOpenFailure.WrongPasswordOrTampered,
                    "Incorrect master password, or this vault file has been altered since it was written.");
            }
            catch (Exception)
            {
                key.Dispose();
                return VaultOpenResult.Fail(VaultOpenFailure.Malformed, "The vault file could not be decrypted.");
            }

            try
            {
                VaultDocument document;
                try
                {
                    string json = Encoding.UTF8.GetString(plaintext);
                    JsonObject payload = JsonValue.Parse(json) as JsonObject;
                    if (payload == null) throw new FormatException("Vault payload is not an object.");
                    document = VaultDocument.Deserialize(payload);
                }
                finally
                {
                    Array.Clear(plaintext, 0, plaintext.Length);
                }

                return VaultOpenResult.Ok(key, parameters, document);
            }
            catch (JsonParseException)
            {
                key.Dispose();
                return VaultOpenResult.Fail(VaultOpenFailure.Malformed,
                    "The master password was accepted but the vault contents are not readable.");
            }
            catch (FormatException ex)
            {
                key.Dispose();
                return VaultOpenResult.Fail(VaultOpenFailure.Malformed,
                    "The vault contents are damaged: " + ex.Message);
            }
            catch (NotSupportedException ex)
            {
                key.Dispose();
                return VaultOpenResult.Fail(VaultOpenFailure.UnsupportedVersion, ex.Message);
            }
        }

        /// <summary>Reads only the key-derivation settings, without a password.</summary>
        /// <remarks>Used by the unlock screen to show what the vault expects before trying.</remarks>
        public static bool TryReadParameters(string path, out KdfParameters parameters)
        {
            parameters = null;
            try
            {
                if (!File.Exists(path)) return false;
                JsonObject root = JsonValue.Parse(Encoding.UTF8.GetString(AtomicFile.ReadAllBytes(path))) as JsonObject;
                if (root == null) return false;
                JsonObject kdf = root.GetObject("kdf");
                if (kdf == null) return false;
                parameters = new KdfParameters(
                    kdf.GetString("algorithm", string.Empty),
                    kdf.GetInt("iterations", 0),
                    Convert.FromBase64String(kdf.GetRequiredString("salt")));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Encrypts and atomically writes the vault.</summary>
        public static void Write(string path, MasterKey key, KdfParameters parameters, VaultDocument document)
        {
            if (key == null) throw new ArgumentNullException("key");
            if (parameters == null) throw new ArgumentNullException("parameters");
            if (document == null) throw new ArgumentNullException("document");
            if (key.IsDisposed) throw new ObjectDisposedException("MasterKey");

            document.ModifiedUtc = DateTime.UtcNow;

            byte[] plaintext = Encoding.UTF8.GetBytes(document.Serialize().ToString());
            SealedMessage message;
            try
            {
                byte[] aad = BuildAssociatedData(CurrentVersion, AuthenticatedCipher.CipherName, parameters);
                message = AuthenticatedCipher.Seal(key.CipherKey, key.MacKey, plaintext, aad);
            }
            finally
            {
                Array.Clear(plaintext, 0, plaintext.Length);
            }

            JsonObject cipher = new JsonObject();
            cipher.Set("algorithm", AuthenticatedCipher.CipherName);
            cipher.Set("iv", Convert.ToBase64String(message.Iv));
            cipher.Set("ciphertext", Convert.ToBase64String(message.Ciphertext));
            cipher.Set("mac", Convert.ToBase64String(message.Mac));

            JsonObject kdf = new JsonObject();
            kdf.Set("algorithm", parameters.Algorithm);
            kdf.Set("iterations", parameters.Iterations);
            kdf.Set("salt", Convert.ToBase64String(parameters.Salt));

            JsonObject root = new JsonObject();
            root.Set("format", FormatMarker);
            root.Set("version", CurrentVersion);
            root.Set("updatedUtc", document.ModifiedUtc.ToString("o", CultureInfo.InvariantCulture));
            root.Set("kdf", kdf);
            root.Set("cipher", cipher);

            byte[] bytes = Encoding.UTF8.GetBytes(root.ToPrettyString());

            // Write first, tighten permissions second: the window in which a freshly created file
            // has inherited permissions is closed as early as possible.
            AtomicFile.WriteAllBytesAtomic(path, bytes);
            AtomicFile.RestrictToCurrentUser(path);
        }

        /// <summary>
        /// Confirms that a freshly written file can actually be read back with the key in hand.
        /// </summary>
        /// <remarks>
        /// Used after writing the vault to a new location. A write that reports success but
        /// produces a file that will not open would strand the user with no way back, so the copy
        /// is proved readable before the old location is forgotten.
        /// </remarks>
        public static bool Verify(string path, MasterKey key, KdfParameters parameters, out string problem)
        {
            problem = null;

            try
            {
                byte[] raw = AtomicFile.ReadAllBytes(path);
                JsonObject root = JsonValue.Parse(Encoding.UTF8.GetString(raw)) as JsonObject;
                if (root == null) { problem = "The written file is not valid JSON."; return false; }

                int version = root.GetInt("version", 0);
                if (version != CurrentVersion)
                {
                    problem = "The written file reports an unexpected version.";
                    return false;
                }

                JsonObject cipher = root.GetObject("cipher");
                if (cipher == null) { problem = "The written file has no cipher section."; return false; }

                string cipherAlgorithm = cipher.GetString("algorithm", string.Empty);
                SealedMessage message = new SealedMessage(
                    Convert.FromBase64String(cipher.GetRequiredString("iv")),
                    Convert.FromBase64String(cipher.GetRequiredString("ciphertext")),
                    Convert.FromBase64String(cipher.GetRequiredString("mac")));

                byte[] plaintext = AuthenticatedCipher.Open(key.CipherKey, key.MacKey, message,
                    BuildAssociatedData(version, cipherAlgorithm, parameters));

                try
                {
                    JsonObject payload = JsonValue.Parse(Encoding.UTF8.GetString(plaintext)) as JsonObject;
                    if (payload == null) { problem = "The written file did not contain a readable vault."; return false; }
                    VaultDocument.Deserialize(payload);
                    return true;
                }
                finally
                {
                    Array.Clear(plaintext, 0, plaintext.Length);
                }
            }
            catch (Exception ex)
            {
                problem = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Data bound into the authentication tag. Version and cipher name are included so an
        /// attacker cannot rewrite the envelope to claim an older, weaker format.
        /// </summary>
        private static byte[] BuildAssociatedData(int version, string cipherAlgorithm, KdfParameters parameters)
        {
            byte[] prefix = Encoding.UTF8.GetBytes(
                FormatMarker + "|" + version.ToString(CultureInfo.InvariantCulture) + "|" + cipherAlgorithm + "|");
            byte[] kdfBytes = parameters.ToAuthenticatedBytes();

            byte[] combined = new byte[prefix.Length + kdfBytes.Length];
            Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
            Buffer.BlockCopy(kdfBytes, 0, combined, prefix.Length, kdfBytes.Length);
            return combined;
        }
    }
}
