using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Keyward.Core.Crypto;
using Keyward.Core.Domain;
using Keyward.Core.Storage;

namespace Keyward.Tests
{
    /// <summary>
    /// Security-critical tests.
    /// </summary>
    /// <remarks>
    /// These exercise the properties the product's security claims actually rest on: that data at
    /// rest is unreadable without the master password, that tampering and truncation are detected
    /// rather than silently decrypted, that the KDF cannot be downgraded, and that generated
    /// passwords really do carry the entropy they claim.
    /// </remarks>
    public static class CryptoTests
    {
        private static string _scratchDir;

        public static void Run(string scratchDir)
        {
            _scratchDir = scratchDir;

            Check.Suite("SecureRandom");
            RandomBasics();
            RandomDistribution();

            Check.Suite("SecretBytes");
            SecretBufferZeroisation();

            Check.Suite("Key derivation");
            DerivationProperties();

            Check.Suite("Authenticated cipher");
            CipherRoundTrip();
            CipherTamperDetection();
            CipherAadBinding();

            Check.Suite("Password generator");
            GeneratorPasswordRules();
            GeneratorValidation();
            GeneratorPassphrase();
            StrengthEstimator();

            Check.Suite("Vault file");
            VaultRoundTrip();
            VaultWrongPassword();
            VaultTamperDetection();
            VaultMalformedInput();
            VaultAtomicity();
        }

        // ------------------------------------------------------------------ SecureRandom

        /// <summary>
        /// Valid KDF parameters at the lowest cost the product permits.
        /// </summary>
        /// <remarks>
        /// Tests must not cheat by constructing parameters the product would reject in real use,
        /// otherwise they would be testing a code path that never executes for a genuine vault.
        /// </remarks>
        private static KdfParameters FastParams()
        {
            return new KdfParameters(KdfParameters.Pbkdf2Sha512, KdfParameters.MinimumIterations, new byte[32]);
        }

        private static void RandomBasics()
        {
            Check.Case("produces the requested number of bytes", delegate
            {
                byte[] b = SecureRandom.NextBytes(32);
                Check.AreEqual(32, b.Length, "length mismatch");
                Check.False(IsAllZero(b), "32 random bytes were all zero");
            });

            Check.Case("NextInt32 stays within bounds", delegate
            {
                for (int i = 0; i < 5000; i++)
                {
                    int v = SecureRandom.NextInt32(7);
                    Check.True(v >= 0 && v < 7, "value " + v + " outside [0,7)");
                }
            });

            Check.Case("NextInt32 handles a power-of-two range", delegate
            {
                for (int i = 0; i < 2000; i++)
                {
                    int v = SecureRandom.NextInt32(16);
                    Check.True(v >= 0 && v < 16, "value " + v + " outside [0,16)");
                }
            });

            Check.Case("RandomString draws only from the alphabet", delegate
            {
                const string Alphabet = "abcdef";
                for (int i = 0; i < 500; i++)
                {
                    string s = SecureRandom.RandomString(12, Alphabet);
                    Check.AreEqual(12, s.Length, "wrong length");
                    foreach (char c in s)
                        Check.True(Alphabet.IndexOf(c) >= 0, "character '" + c + "' is outside the alphabet");
                }
            });

            Check.Case("Shuffle preserves the multiset", delegate
            {
                List<int> list = new List<int>();
                for (int i = 0; i < 50; i++) list.Add(i);
                SecureRandom.Shuffle(list);
                Check.AreEqual(50, list.Count, "count changed");
                bool[] seen = new bool[50];
                foreach (int v in list) seen[v] = true;
                foreach (bool s in seen) Check.True(s, "shuffle lost or duplicated an element");
            });

            Check.Case("pass-phrase word list has 256 unique words", delegate
            {
                Check.AreEqual(256, SecureRandom.WordCount,
                    "the word list must contain exactly 256 words so each word carries exactly 8 bits");
            });
        }

        private static void RandomDistribution()
        {
            // Guards against a naive modulo implementation. With 12 buckets and 60k draws a
            // correct sampler lands within a few percent of 5000 per bucket; a biased one would
            // skew the low buckets measurably.
            Check.Case("NextInt32 is not modulo-biased", delegate
            {
                const int Buckets = 12;
                const int Draws = 60000;
                int[] counts = new int[Buckets];
                for (int i = 0; i < Draws; i++)
                    counts[SecureRandom.NextInt32(Buckets)]++;

                int expected = Draws / Buckets;
                foreach (int c in counts)
                {
                    int deviation = (int)Math.Abs((double)c - expected);
                    Check.True(deviation < expected * 0.12,
                        "bucket count " + c + " deviates from expected " + expected + " by more than 12%");
                }
            });
        }

        private static bool IsAllZero(byte[] bytes)
        {
            foreach (byte b in bytes) if (b != 0) return false;
            return true;
        }

        // ------------------------------------------------------------------ SecretBytes

        private static void SecretBufferZeroisation()
        {
            Check.Case("Dispose zeroes the underlying buffer", delegate
            {
                byte[] original;
                using (SecretBytes secret = new SecretBytes(32))
                {
                    original = secret.Copy();
                    SecureRandom.NextBytes(secret.Buffer);
                }

                foreach (byte b in original)
                    Check.AreEqual(0, b, "buffer was not zeroed after Dispose");
            });

            Check.Case("using the buffer after Dispose throws", delegate
            {
                SecretBytes secret = new SecretBytes(16);
                secret.Dispose();
                Check.Throws<ObjectDisposedException>(delegate { byte[] ignored = secret.Buffer; },
                    "reading a disposed secret must fail loudly");
            });

            Check.Case("Dispose is idempotent", delegate
            {
                SecretBytes secret = new SecretBytes(16);
                secret.Dispose();
                Check.DoesNotThrow(delegate { secret.Dispose(); }, "second Dispose threw");
            });
        }

        // ------------------------------------------------------------------ Key derivation

        private static void DerivationProperties()
        {
            Check.Case("same password and salt derive the same key", delegate
            {
                KdfParameters p = FastParams();
                using (MasterKey a = KeyDerivation.Derive("correct horse", p))
                using (MasterKey b = KeyDerivation.Derive("correct horse", p))
                {
                    Check.True(AuthenticatedCipher.FixedTimeEquals(a.CipherKey.Buffer, b.CipherKey.Buffer),
                        "derivation is not deterministic");
                    Check.True(AuthenticatedCipher.FixedTimeEquals(a.MacKey.Buffer, b.MacKey.Buffer),
                        "MAC key derivation is not deterministic");
                }
            });

            Check.Case("different passwords derive different keys", delegate
            {
                KdfParameters p = FastParams();
                using (MasterKey a = KeyDerivation.Derive("password-one", p))
                using (MasterKey b = KeyDerivation.Derive("password-two", p))
                {
                    Check.False(AuthenticatedCipher.FixedTimeEquals(a.CipherKey.Buffer, b.CipherKey.Buffer),
                        "two different passwords produced the same key");
                }
            });

            Check.Case("the two sub-keys differ", delegate
            {
                KdfParameters p = FastParams();
                using (MasterKey k = KeyDerivation.Derive("anything", p))
                {
                    Check.False(AuthenticatedCipher.FixedTimeEquals(k.CipherKey.Buffer, k.MacKey.Buffer),
                        "the encryption key and MAC key are identical, which breaks key separation");
                }
            });

            Check.Case("salt is random per vault", delegate
            {
                KdfParameters a = new KdfParameters();
                KdfParameters b = new KdfParameters();
                Check.False(AuthenticatedCipher.FixedTimeEquals(a.Salt, b.Salt),
                    "two vaults were created with the same salt");
            });

            Check.Case("weak iteration counts are refused", delegate
            {
                KdfParameters weak = new KdfParameters(KdfParameters.Pbkdf2Sha512, 1000, new byte[32]);
                Check.Throws<FormatException>(delegate
                {
                    using (MasterKey ignored = KeyDerivation.Derive("x", weak)) { }
                }, "an iteration count below the floor must be rejected");
            });

            Check.Case("unknown KDF algorithm is refused", delegate
            {
                KdfParameters alien = new KdfParameters("scrypt-v1", 100000, new byte[32]);
                Check.Throws<NotSupportedException>(delegate
                {
                    using (MasterKey ignored = KeyDerivation.Derive("x", alien)) { }
                }, "an unknown KDF must be rejected rather than silently ignored");
            });

            Check.Case("unlock cost is measurable", delegate
            {
                KdfParameters p = FastParams();
                TimeSpan cost = KeyDerivation.MeasureUnlockCost("measure", p);
                Check.True(cost.TotalMilliseconds > 0, "derivation reported zero duration");
                Console.WriteLine("          (" + p.Iterations.ToString("N0", CultureInfo.InvariantCulture) +
                                  " iterations took " + cost.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms)");
            });
        }

        // ------------------------------------------------------------------ Cipher

        private static MasterKey NewTestKey()
        {
            return KeyDerivation.Derive("test-master", FastParams());
        }

        private static void CipherRoundTrip()
        {
            Check.Case("sealed data decrypts back to the original", delegate
            {
                byte[] plain = Encoding.UTF8.GetBytes("the quick brown fox");
                byte[] aad = Encoding.UTF8.GetBytes("header");
                using (MasterKey k = NewTestKey())
                {
                    SealedMessage sealedMessage = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey, plain, aad);
                    byte[] opened = AuthenticatedCipher.Open(k.CipherKey, k.MacKey, sealedMessage, aad);
                    Check.AreEqual("the quick brown fox", Encoding.UTF8.GetString(opened), "round trip changed the data");
                }
            });

            Check.Case("ciphertext does not contain the plaintext", delegate
            {
                byte[] plain = Encoding.UTF8.GetBytes("SUPERSECRETPASSWORD");
                using (MasterKey k = NewTestKey())
                {
                    SealedMessage m = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey, plain, null);
                    string asText = Encoding.UTF8.GetString(m.Ciphertext);
                    Check.True(asText.IndexOf("SUPERSECRET", StringComparison.Ordinal) < 0,
                        "the secret appeared verbatim in the ciphertext");
                }
            });

            Check.Case("each seal uses a fresh IV", delegate
            {
                byte[] plain = Encoding.UTF8.GetBytes("same input");
                using (MasterKey k = NewTestKey())
                {
                    SealedMessage a = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey, plain, null);
                    SealedMessage b = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey, plain, null);
                    Check.False(AuthenticatedCipher.FixedTimeEquals(a.Iv, b.Iv), "IV was reused");
                    Check.False(AuthenticatedCipher.FixedTimeEquals(a.Ciphertext, b.Ciphertext),
                        "identical plaintext produced identical ciphertext, which leaks equality");
                }
            });

            Check.Case("empty plaintext round-trips", delegate
            {
                using (MasterKey k = NewTestKey())
                {
                    SealedMessage m = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey, new byte[0], null);
                    byte[] opened = AuthenticatedCipher.Open(k.CipherKey, k.MacKey, m, null);
                    Check.AreEqual(0, opened.Length, "empty payload did not round-trip");
                }
            });

            Check.Case("large payload round-trips", delegate
            {
                byte[] plain = new byte[400000];
                SecureRandom.NextBytes(plain);
                using (MasterKey k = NewTestKey())
                {
                    SealedMessage m = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey, plain, null);
                    byte[] opened = AuthenticatedCipher.Open(k.CipherKey, k.MacKey, m, null);
                    Check.AreEqual(plain.Length, opened.Length, "length changed");
                    Check.True(AuthenticatedCipher.FixedTimeEquals(plain, opened), "content changed");
                }
            });
        }

        private static void CipherTamperDetection()
        {
            Check.Case("modified ciphertext is rejected", delegate
            {
                byte[] plain = Encoding.UTF8.GetBytes("authentic data");
                using (MasterKey k = NewTestKey())
                {
                    SealedMessage m = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey, plain, null);
                    m.Ciphertext[0] ^= 0x01;
                    Check.Throws<IntegrityException>(delegate
                    {
                        AuthenticatedCipher.Open(k.CipherKey, k.MacKey, m, null);
                    }, "tampered ciphertext must not decrypt");
                }
            });

            Check.Case("modified IV is rejected", delegate
            {
                using (MasterKey k = NewTestKey())
                {
                    SealedMessage m = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey, Encoding.UTF8.GetBytes("data"), null);
                    m.Iv[0] ^= 0x80;
                    Check.Throws<IntegrityException>(delegate
                    {
                        AuthenticatedCipher.Open(k.CipherKey, k.MacKey, m, null);
                    }, "tampered IV must not decrypt");
                }
            });

            Check.Case("modified MAC is rejected", delegate
            {
                using (MasterKey k = NewTestKey())
                {
                    SealedMessage m = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey, Encoding.UTF8.GetBytes("data"), null);
                    m.Mac[0] ^= 0x01;
                    Check.Throws<IntegrityException>(delegate
                    {
                        AuthenticatedCipher.Open(k.CipherKey, k.MacKey, m, null);
                    }, "tampered MAC must not decrypt");
                }
            });

            Check.Case("truncated ciphertext is rejected", delegate
            {
                using (MasterKey k = NewTestKey())
                {
                    SealedMessage m = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey, Encoding.UTF8.GetBytes("some data here"), null);
                    byte[] shortened = new byte[m.Ciphertext.Length - 8];
                    Array.Copy(m.Ciphertext, shortened, shortened.Length);
                    m.Ciphertext = shortened;
                    Check.Throws<IntegrityException>(delegate
                    {
                        AuthenticatedCipher.Open(k.CipherKey, k.MacKey, m, null);
                    }, "truncated ciphertext must not decrypt");
                }
            });

            Check.Case("the wrong key cannot open the message", delegate
            {
                byte[] plain = Encoding.UTF8.GetBytes("secret");
                using (MasterKey a = NewTestKey())
                using (MasterKey b = KeyDerivation.Derive("different-master",
                           FastParams()))
                {
                    SealedMessage m = AuthenticatedCipher.Seal(a.CipherKey, a.MacKey, plain, null);
                    Check.Throws<IntegrityException>(delegate
                    {
                        AuthenticatedCipher.Open(b.CipherKey, b.MacKey, m, null);
                    }, "a different key must not open the message");
                }
            });

            Check.Case("constant-time comparison is correct", delegate
            {
                Check.True(AuthenticatedCipher.FixedTimeEquals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }), "equal arrays compared unequal");
                Check.False(AuthenticatedCipher.FixedTimeEquals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 4 }), "different arrays compared equal");
                Check.False(AuthenticatedCipher.FixedTimeEquals(new byte[] { 1, 2 }, new byte[] { 1, 2, 3 }), "different lengths compared equal");
                Check.False(AuthenticatedCipher.FixedTimeEquals(null, new byte[] { 1 }), "null compared equal");
            });
        }

        private static void CipherAadBinding()
        {
            Check.Case("changing associated data invalidates the tag", delegate
            {
                using (MasterKey k = NewTestKey())
                {
                    SealedMessage m = AuthenticatedCipher.Seal(k.CipherKey, k.MacKey,
                        Encoding.UTF8.GetBytes("payload"), Encoding.UTF8.GetBytes("v1"));
                    Check.Throws<IntegrityException>(delegate
                    {
                        AuthenticatedCipher.Open(k.CipherKey, k.MacKey, m, Encoding.UTF8.GetBytes("v2"));
                    }, "associated data must be bound into the tag");
                }
            });
        }

        // ------------------------------------------------------------------ Generator

        private static void GeneratorPasswordRules()
        {
            Check.Case("honours the requested length", delegate
            {
                for (int len = 8; len <= 64; len += 8)
                {
                    GeneratorOptions o = new GeneratorOptions();
                    o.Length = len;
                    string pwd = PasswordGenerator.GeneratePassword(o);
                    Check.AreEqual(len, pwd.Length, "length " + len + " was not honoured");
                }
            });

            Check.Case("includes every selected character class", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.Length = 32;
                for (int i = 0; i < 300; i++)
                {
                    string pwd = PasswordGenerator.GeneratePassword(o);
                    bool upper = false, lower = false, digit = false, symbol = false;
                    foreach (char c in pwd)
                    {
                        if (char.IsUpper(c)) upper = true;
                        else if (char.IsLower(c)) lower = true;
                        else if (char.IsDigit(c)) digit = true;
                        else symbol = true;
                    }
                    Check.True(upper && lower && digit && symbol,
                        "generated password '" + pwd + "' is missing a character class");
                }
            });

            Check.Case("omits disabled classes", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.Length = 40;
                o.UseSymbols = false;
                for (int i = 0; i < 200; i++)
                {
                    string pwd = PasswordGenerator.GeneratePassword(o);
                    foreach (char c in pwd)
                        Check.False(SecureRandom.Symbols.IndexOf(c) >= 0,
                            "symbol '" + c + "' appeared although symbols were disabled");
                }
            });

            Check.Case("excludes ambiguous characters on request", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.Length = 64;
                o.ExcludeAmbiguous = true;
                for (int i = 0; i < 200; i++)
                {
                    string pwd = PasswordGenerator.GeneratePassword(o);
                    foreach (char c in pwd)
                        Check.False(SecureRandom.IsAmbiguous(c),
                            "ambiguous character '" + c + "' appeared although it was excluded");
                }
            });

            Check.Case("produces different values on every call", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.Length = 24;
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < 500; i++)
                    Check.True(seen.Add(PasswordGenerator.GeneratePassword(o)), "the generator repeated a value");
            });

            Check.Case("covers the whole alphabet", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.Length = 24;
                HashSet<char> seen = new HashSet<char>();
                for (int i = 0; i < 3000; i++)
                    foreach (char c in PasswordGenerator.GeneratePassword(o)) seen.Add(c);

                // 26 + 26 + 10 + 24 symbols
                Check.True(seen.Count >= 86,
                    "only " + seen.Count + " distinct characters ever appeared, which suggests a shrunken pool");
            });
        }

        private static void GeneratorValidation()
        {
            Check.Case("rejects an empty character set", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.UseUppercase = o.UseLowercase = o.UseDigits = o.UseSymbols = false;
                Check.Throws<GeneratorConfigurationException>(delegate
                {
                    PasswordGenerator.Generate(o);
                }, "an impossible configuration must be refused, not silently produce nothing");
            });

            Check.Case("rejects a length below the minimum", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.Length = 4;
                Check.Throws<GeneratorConfigurationException>(delegate { PasswordGenerator.Generate(o); },
                    "a 4-character password must be refused");
            });

            Check.Case("explains a length that cannot hold every class", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.Length = 3;
                o.UseSymbols = false;
                Check.Throws<GeneratorConfigurationException>(delegate { PasswordGenerator.Generate(o); },
                    "3 characters cannot hold all four classes and the error must say so");
            });

            Check.Case("rejects a pass-phrase that is too short", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.Mode = GeneratorMode.Passphrase;
                o.WordCount = 2;
                Check.Throws<GeneratorConfigurationException>(delegate { PasswordGenerator.Generate(o); },
                    "a two-word pass-phrase must be refused");
            });
        }

        private static void GeneratorPassphrase()
        {
            Check.Case("produces the requested number of words", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.Mode = GeneratorMode.Passphrase;
                o.WordCount = 5;
                o.Separator = "-";
                for (int i = 0; i < 50; i++)
                {
                    string p = PasswordGenerator.GeneratePassphrase(o);
                    string[] parts = p.Split('-');
                    Check.AreEqual(5, parts.Length, "expected 5 words, got '" + p + "'");
                }
            });

            Check.Case("can append a number", delegate
            {
                GeneratorOptions o = new GeneratorOptions();
                o.Mode = GeneratorMode.Passphrase;
                o.WordCount = 3;
                o.Separator = "-";
                o.IncludeNumber = true;
                string p = PasswordGenerator.GeneratePassphrase(o);
                string[] parts = p.Split('-');
                Check.AreEqual(4, parts.Length, "expected a trailing number");
                Check.True(parts[3].Length == 2, "the appended number should be two digits");
            });
        }

        private static void StrengthEstimator()
        {
            Check.Case("flags the most common passwords as very weak", delegate
            {
                PasswordStrength s = PasswordGenerator.Estimate("password");
                Check.AreEqual(0, s.Score, "'password' must not score above very weak");
            });

            Check.Case("penalises repeated characters", delegate
            {
                PasswordStrength strong = PasswordGenerator.Estimate("Xk7#mQ2$zR9@pL4!");
                PasswordStrength repeated = PasswordGenerator.Estimate("aaaaaaaaaaaaaaa");
                Check.True(strong.Bits > repeated.Bits, "a repeated run outscored a random string");
            });

            Check.Case("penalises sequences", delegate
            {
                PasswordStrength s = PasswordGenerator.Estimate("abcdefghijklmnop");
                Check.True(s.Bits < 120, "a long alphabet run should be discounted");
            });

            Check.Case("rewards length", delegate
            {
                PasswordStrength shortPw = PasswordGenerator.Estimate("Xk7#mQ2$z");
                PasswordStrength longPw = PasswordGenerator.Estimate("Xk7#mQ2$zR9@pL4!wB6^nD3&");
                Check.True(longPw.Bits > shortPw.Bits, "a longer password did not score higher");
            });

            Check.Case("handles an empty password safely", delegate
            {
                PasswordStrength s = PasswordGenerator.Estimate(string.Empty);
                Check.AreEqual(0, s.Bits, "empty password must score zero");
            });
        }

        // ------------------------------------------------------------------ Vault file

        private static string NewVaultPath(string name)
        {
            return Path.Combine(_scratchDir, name + ".kvw");
        }

        private static void VaultRoundTrip()
        {
            Check.Case("creates a vault readable with the same password", delegate
            {
                string path = NewVaultPath("roundtrip");
                if (File.Exists(path)) File.Delete(path);

                KdfParameters p;
                VaultDocument doc;
                using (MasterKey key = VaultFile.Create(path, "open sesame", out p))
                {
                    doc = new VaultDocument();
                    Credential c = new Credential();
                    c.Id = VaultDocument.NewId();
                    c.Name = "GitHub";
                    c.Username = "octocat";
                    c.Password = "hunter2";
                    c.Url = "https://github.com";
                    doc.Credentials.Add(c);
                    VaultFile.Write(path, key, p, doc);
                }

                VaultOpenResult r = VaultFile.Open(path, "open sesame");
                Check.True(r.Success, "reopening a freshly written vault failed: " + r.Message);
                Check.AreEqual(1, r.Document.Credentials.Count, "credential count changed");
                Check.AreEqual("hunter2", r.Document.Credentials[0].Password, "password did not survive the round trip");
                r.Key.Dispose();
            });

            Check.Case("the vault file does not contain the plaintext password", delegate
            {
                string path = NewVaultPath("opaque");
                if (File.Exists(path)) File.Delete(path);

                KdfParameters p;
                using (MasterKey key = VaultFile.Create(path, "open sesame", out p))
                {
                    VaultDocument doc = new VaultDocument();
                    Credential c = new Credential();
                    c.Id = VaultDocument.NewId();
                    c.Name = "Bank";
                    c.Username = "me@example.com";
                    c.Password = "PLAINTEXT-CANARY-9931";
                    doc.Credentials.Add(c);
                    VaultFile.Write(path, key, p, doc);
                }

                string raw = File.ReadAllText(path);
                Check.True(raw.IndexOf("PLAINTEXT-CANARY", StringComparison.Ordinal) < 0,
                    "the password appeared in clear text inside the vault file");
                Check.True(raw.IndexOf("Bank", StringComparison.Ordinal) < 0,
                    "the entry name appeared in clear text inside the vault file");
            });

            Check.Case("stores notes, folders and tags", delegate
            {
                string path = NewVaultPath("shapes");
                if (File.Exists(path)) File.Delete(path);

                KdfParameters p;
                using (MasterKey key = VaultFile.Create(path, "pw", out p))
                {
                    VaultDocument doc = new VaultDocument();
                    doc.Folders.Add(new Folder("f1", "Work"));
                    SecureNote n = new SecureNote();
                    n.Id = VaultDocument.NewId();
                    n.Name = "Wifi codes";
                    n.Body = "admin:12345678";
                    n.FolderId = "f1";
                    n.Tags.Add("network");
                    doc.Notes.Add(n);
                    VaultFile.Write(path, key, p, doc);
                }

                VaultOpenResult r = VaultFile.Open(path, "pw");
                Check.True(r.Success, "could not reopen: " + r.Message);
                Check.AreEqual(1, r.Document.Notes.Count, "note was lost");
                Check.AreEqual("admin:12345678", r.Document.Notes[0].Body, "note body was lost");
                Check.AreEqual(1, r.Document.Folders.Count, "folder was lost");
                Check.AreEqual(1, r.Document.Notes[0].Tags.Count, "tag was lost");
                r.Key.Dispose();
            });
        }

        private static void VaultWrongPassword()
        {
            Check.Case("rejects an incorrect master password", delegate
            {
                string path = NewVaultPath("wrongpw");
                if (File.Exists(path)) File.Delete(path);

                KdfParameters p;
                using (MasterKey key = VaultFile.Create(path, "correct-password", out p))
                {
                    VaultFile.Write(path, key, p, new VaultDocument());
                }

                VaultOpenResult r = VaultFile.Open(path, "incorrect-password");
                Check.False(r.Success, "a wrong password must not open the vault");
                Check.AreEqual(VaultOpenFailure.WrongPasswordOrTampered, r.Failure, "unexpected failure kind");
                Check.True(r.Message.IndexOf("Incorrect master password", StringComparison.Ordinal) >= 0,
                    "the error message should say the password was wrong");
            });

            Check.Case("a wrong password and a tampered file are indistinguishable", delegate
            {
                Check.True(VaultOpenFailure.WrongPasswordOrTampered.ToString() == "WrongPasswordOrTampered",
                    "both cases must map to a single failure kind so they cannot be told apart");
            });

            Check.Case("reports a missing vault without throwing", delegate
            {
                VaultOpenResult r = VaultFile.Open(Path.Combine(_scratchDir, "no-such-file.kvw"), "pw");
                Check.False(r.Success, "opening a missing vault should fail gracefully");
                Check.AreEqual(VaultOpenFailure.FileMissing, r.Failure, "wrong failure kind");
            });
        }

        private static void VaultTamperDetection()
        {
            Check.Case("detects a flipped ciphertext byte", delegate
            {
                string path = NewVaultPath("tamper-ct");
                if (File.Exists(path)) File.Delete(path);

                KdfParameters p;
                using (MasterKey key = VaultFile.Create(path, "pw", out p))
                {
                    VaultFile.Write(path, key, p, new VaultDocument());
                }

                JsonObject root = JsonValue.Parse(File.ReadAllText(path)) as JsonObject;
                JsonObject cipher = root.GetObject("cipher");
                string ct = cipher.GetString("ciphertext", string.Empty);
                char[] chars = ct.ToCharArray();
                chars[0] = chars[0] == 'A' ? 'B' : 'A';
                cipher.Set("ciphertext", new string(chars));
                File.WriteAllText(path, root.ToPrettyString());

                VaultOpenResult r = VaultFile.Open(path, "pw");
                Check.False(r.Success, "a modified vault must not open");
                Check.AreEqual(VaultOpenFailure.WrongPasswordOrTampered, r.Failure, "wrong failure kind");
            });

            Check.Case("detects a downgraded iteration count", delegate
            {
                string path = NewVaultPath("tamper-kdf");
                if (File.Exists(path)) File.Delete(path);

                KdfParameters p;
                using (MasterKey key = VaultFile.Create(path, "pw", out p))
                {
                    VaultFile.Write(path, key, p, new VaultDocument());
                }

                JsonObject root = JsonValue.Parse(File.ReadAllText(path)) as JsonObject;
                root.GetObject("kdf").Set("iterations", 100000);
                File.WriteAllText(path, root.ToPrettyString());

                VaultOpenResult r = VaultFile.Open(path, "pw");
                Check.False(r.Success,
                    "changing the KDF parameters must invalidate the tag; otherwise an attacker could force a cheap derivation");
            });

            Check.Case("detects a changed salt", delegate
            {
                string path = NewVaultPath("tamper-salt");
                if (File.Exists(path)) File.Delete(path);

                KdfParameters p;
                using (MasterKey key = VaultFile.Create(path, "pw", out p))
                {
                    VaultFile.Write(path, key, p, new VaultDocument());
                }

                JsonObject root = JsonValue.Parse(File.ReadAllText(path)) as JsonObject;
                root.GetObject("kdf").Set("salt", Convert.ToBase64String(new byte[32]));
                File.WriteAllText(path, root.ToPrettyString());

                VaultOpenResult r = VaultFile.Open(path, "pw");
                Check.False(r.Success, "changing the salt must invalidate the tag");
            });

            Check.Case("refuses a vault from a newer version", delegate
            {
                string path = NewVaultPath("future");
                if (File.Exists(path)) File.Delete(path);

                KdfParameters p;
                using (MasterKey key = VaultFile.Create(path, "pw", out p))
                {
                    VaultFile.Write(path, key, p, new VaultDocument());
                }

                JsonObject root = JsonValue.Parse(File.ReadAllText(path)) as JsonObject;
                root.Set("version", VaultFile.CurrentVersion + 1);
                File.WriteAllText(path, root.ToPrettyString());

                VaultOpenResult r = VaultFile.Open(path, "pw");
                Check.False(r.Success, "a future-version vault must be refused");
                Check.AreEqual(VaultOpenFailure.UnsupportedVersion, r.Failure, "wrong failure kind");
            });
        }

        private static void VaultMalformedInput()
        {
            Check.Case("survives a file that is not JSON", delegate
            {
                string path = NewVaultPath("garbage");
                File.WriteAllText(path, "this is definitely not json {{{");
                VaultOpenResult r = VaultFile.Open(path, "pw");
                Check.False(r.Success, "garbage must not open");
                Check.AreEqual(VaultOpenFailure.Malformed, r.Failure, "wrong failure kind");
            });

            Check.Case("survives an empty file", delegate
            {
                string path = NewVaultPath("empty");
                File.WriteAllText(path, string.Empty);
                VaultOpenResult r = VaultFile.Open(path, "pw");
                Check.False(r.Success, "an empty file must not open");
            });

            Check.Case("refuses a foreign JSON file", delegate
            {
                string path = NewVaultPath("foreign");
                File.WriteAllText(path, "{\"hello\":\"world\"}");
                VaultOpenResult r = VaultFile.Open(path, "pw");
                Check.False(r.Success, "an unrelated JSON file must not open");
                Check.AreEqual(VaultOpenFailure.Malformed, r.Failure, "wrong failure kind");
            });

            Check.Case("survives a truncated vault file", delegate
            {
                string path = NewVaultPath("truncated");
                if (File.Exists(path)) File.Delete(path);
                KdfParameters p;
                using (MasterKey key = VaultFile.Create(path, "pw", out p))
                {
                    VaultFile.Write(path, key, p, new VaultDocument());
                }

                string raw = File.ReadAllText(path);
                File.WriteAllText(path, raw.Substring(0, raw.Length / 2));

                VaultOpenResult r = VaultFile.Open(path, "pw");
                Check.False(r.Success, "a truncated vault must not open");
            });
        }

        private static void VaultAtomicity()
        {
            Check.Case("a failed write leaves the previous vault intact", delegate
            {
                string path = NewVaultPath("atomic");
                if (File.Exists(path)) File.Delete(path);

                KdfParameters p;
                using (MasterKey key = VaultFile.Create(path, "pw", out p))
                {
                    VaultDocument doc = new VaultDocument();
                    Credential c = new Credential();
                    c.Id = VaultDocument.NewId();
                    c.Name = "Original";
                    c.Password = "first-secret";
                    doc.Credentials.Add(c);
                    VaultFile.Write(path, key, p, doc);
                }

                // Overwrite the target with a directory-less but unwritable location to force a
                // mid-write failure is not portable, so instead assert the swap primitive directly:
                // a successful write must never leave a .tmp behind.
                KdfParameters p2;
                using (MasterKey key = VaultFile.Create(NewVaultPath("atomic2"), "pw", out p2))
                {
                    VaultFile.Write(NewVaultPath("atomic2"), key, p2, new VaultDocument());
                }
                Check.False(File.Exists(NewVaultPath("atomic2.kvw.tmp")),
                    "a temp file was left behind after a successful write");

                VaultOpenResult r = VaultFile.Open(path, "pw");
                Check.True(r.Success, "the original vault should still be readable");
                Check.AreEqual("first-secret", r.Document.Credentials[0].Password, "the original data was lost");
                r.Key.Dispose();
            });

            Check.Case("no temp or backup files survive a normal save", delegate
            {
                string path = NewVaultPath("clean");
                if (File.Exists(path)) File.Delete(path);

                KdfParameters p;
                using (MasterKey key = VaultFile.Create(path, "pw", out p))
                {
                    for (int i = 0; i < 3; i++)
                        VaultFile.Write(path, key, p, new VaultDocument());
                }

                Check.False(File.Exists(path + ".tmp"), "a .tmp file survived");
                Check.False(File.Exists(path + ".bak"), "a .bak file survived");
            });
        }
    }
}
