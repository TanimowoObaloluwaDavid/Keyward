using System;
using System.Collections.Generic;
using System.Text;

namespace Keyward.Core.Crypto
{
    /// <summary>Raised when a generator configuration cannot produce anything.</summary>
    public class GeneratorConfigurationException : Exception
    {
        public GeneratorConfigurationException(string message) : base(message) { }
    }

    /// <summary>What the generator should produce.</summary>
    public enum GeneratorMode
    {
        Password = 0,
        Passphrase = 1
    }

    /// <summary>User-selectable generator settings.</summary>
    public sealed class GeneratorOptions
    {
        public GeneratorMode Mode = GeneratorMode.Password;
        public int Length = 20;
        public bool UseUppercase = true;
        public bool UseLowercase = true;
        public bool UseDigits = true;
        public bool UseSymbols = true;
        public bool ExcludeAmbiguous;
        public bool RequireEachClass = true;

        // Passphrase
        public int WordCount = 4;
        public string Separator = "-";
        public bool CapitalizeWords;
        public bool IncludeNumber;

        public GeneratorOptions Clone()
        {
            GeneratorOptions copy = new GeneratorOptions();
            copy.Mode = Mode;
            copy.Length = Length;
            copy.UseUppercase = UseUppercase;
            copy.UseLowercase = UseLowercase;
            copy.UseDigits = UseDigits;
            copy.UseSymbols = UseSymbols;
            copy.ExcludeAmbiguous = ExcludeAmbiguous;
            copy.RequireEachClass = RequireEachClass;
            copy.WordCount = WordCount;
            copy.Separator = Separator;
            copy.CapitalizeWords = CapitalizeWords;
            copy.IncludeNumber = IncludeNumber;
            return copy;
        }

        /// <summary>Returns the effective character pool, or throws if it is empty.</summary>
        public string BuildAlphabet()
        {
            StringBuilder sb = new StringBuilder();

            if (UseUppercase) sb.Append(SecureRandom.UpperAlpha);
            if (UseLowercase) sb.Append(SecureRandom.LowerAlpha);
            if (UseDigits) sb.Append(SecureRandom.Digits);
            if (UseSymbols) sb.Append(SecureRandom.Symbols);

            string alphabet = sb.ToString();

            if (ExcludeAmbiguous)
            {
                StringBuilder filtered = new StringBuilder(alphabet.Length);
                foreach (char c in alphabet)
                    if (!SecureRandom.IsAmbiguous(c)) filtered.Append(c);
                alphabet = filtered.ToString();
            }

            if (alphabet.Length == 0)
                throw new GeneratorConfigurationException(
                    "Select at least one character type. Every type is currently switched off.");

            return alphabet;
        }

        /// <summary>Validates the configuration and explains what is wrong if it is unusable.</summary>
        public void Validate()
        {
            if (Mode == GeneratorMode.Password)
            {
                if (Length < 8)
                    throw new GeneratorConfigurationException("A generated password must be at least 8 characters.");
                if (Length > 256)
                    throw new GeneratorConfigurationException("Passwords are limited to 256 characters.");

                if (!UseUppercase && !UseLowercase && !UseDigits && !UseSymbols)
                    throw new GeneratorConfigurationException(
                        "Select at least one character type. Every type is currently switched off.");

                if (RequireEachClass)
                {
                    int classes = 0;
                    if (UseUppercase) classes++;
                    if (UseLowercase) classes++;
                    if (UseDigits) classes++;
                    if (UseSymbols) classes++;
                    if (Length < classes)
                        throw new GeneratorConfigurationException(
                            "A password of " + Length + " characters is too short to include every selected character type. " +
                            "Increase the length or turn off 'Include each type'.");
                }
            }
            else
            {
                if (WordCount < 3)
                    throw new GeneratorConfigurationException("A pass-phrase needs at least 3 words.");
                if (WordCount > 16)
                    throw new GeneratorConfigurationException("A pass-phrase is limited to 16 words.");
                if (Separator != null && Separator.Length > 4)
                    throw new GeneratorConfigurationException("The separator is limited to 4 characters.");
            }
        }
    }

    /// <summary>Generates passwords and pass-phrases using the OS cryptographic RNG.</summary>
    public static class PasswordGenerator
    {
        /// <summary>Generates a value according to <paramref name="options"/>.</summary>
        public static string Generate(GeneratorOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            options.Validate();

            return options.Mode == GeneratorMode.Passphrase
                ? GeneratePassphrase(options)
                : GeneratePassword(options);
        }

        /// <summary>
        /// Builds a random password. When <see cref="GeneratorOptions.RequireEachClass"/> is set,
        /// one character from every enabled class is placed first and the remainder is filled from
        /// the whole pool, then shuffled. This guarantees the password actually contains an
        /// uppercase letter, a lowercase letter and so on, without biasing the result.
        /// </summary>
        public static string GeneratePassword(GeneratorOptions options)
        {
            string alphabet = options.BuildAlphabet();

            char[] chars = new char[options.Length];

            if (options.RequireEachClass)
            {
                List<string> required = new List<string>(4);
                AddClass(required, options.UseUppercase, SecureRandom.UpperAlpha, options.ExcludeAmbiguous);
                AddClass(required, options.UseLowercase, SecureRandom.LowerAlpha, options.ExcludeAmbiguous);
                AddClass(required, options.UseDigits, SecureRandom.Digits, options.ExcludeAmbiguous);
                AddClass(required, options.UseSymbols, SecureRandom.Symbols, options.ExcludeAmbiguous);

                int i = 0;
                foreach (string set in required)
                {
                    chars[i] = set[SecureRandom.NextInt32(set.Length)];
                    i++;
                }

                for (; i < chars.Length; i++)
                    chars[i] = alphabet[SecureRandom.NextInt32(alphabet.Length)];
            }
            else
            {
                for (int i = 0; i < chars.Length; i++)
                    chars[i] = alphabet[SecureRandom.NextInt32(alphabet.Length)];
            }

            // Shuffle so the guaranteed characters are not always in the same positions, which
            // would make the distribution of generated passwords visibly non-uniform.
            SecureRandom.Shuffle(chars);

            return new string(chars);
        }

        private static void AddClass(List<string> into, bool enabled, string source, bool excludeAmbiguous)
        {
            if (!enabled) return;

            string set = source;
            if (excludeAmbiguous)
            {
                StringBuilder sb = new StringBuilder(source.Length);
                foreach (char c in source)
                    if (!SecureRandom.IsAmbiguous(c)) sb.Append(c);
                set = sb.ToString();
            }

            if (set.Length > 0) into.Add(set);
        }

        /// <summary>
        /// Builds a pass-phrase from the bundled 256-word list. Each word contributes 8 bits, so
        /// a four-word pass-phrase carries 32 bits of generator entropy; an optional appended
        /// number adds about 10.
        /// </summary>
        public static string GeneratePassphrase(GeneratorOptions options)
        {
            string separator = options.Separator ?? string.Empty;
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < options.WordCount; i++)
            {
                if (i > 0) sb.Append(separator);

                string word = SecureRandom.RandomWord();
                if (options.CapitalizeWords && word.Length > 0)
                    word = char.ToUpperInvariant(word[0]) + word.Substring(1);

                sb.Append(word);
            }

            if (options.IncludeNumber)
            {
                sb.Append(separator);
                // Two digits: enough to meaningfully break a dictionary attack, short enough to
                // stay typeable.
                sb.Append(SecureRandom.NextInt32(10).ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(SecureRandom.NextInt32(10).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Estimates the strength of a user-chosen password in bits, and explains the biggest
        /// problem found.
        /// </summary>
        /// <remarks>
        /// This is a heuristic, not a proof. It deliberately models the attacks that actually work
        /// against human-chosen passwords (dictionary words, repeated characters, simple
        /// substitutions, keyboard runs) rather than only measuring raw length. The UI presents it
        /// as guidance, never as a guarantee.
        /// </remarks>
        public static PasswordStrength Estimate(string password)
        {
            return PasswordStrengthEstimator.Estimate(password);
        }
    }

    /// <summary>Result of a strength estimate.</summary>
    public struct PasswordStrength
    {
        /// <summary>Estimated search space in bits.</summary>
        public int Bits;

        /// <summary>0 (unusable) to 4 (excellent).</summary>
        public int Score;

        public string Label;

        /// <summary>Human-readable reason the score is not higher.</summary>
        public string Advice;

        public bool HasUppercase;
        public bool HasLowercase;
        public bool HasDigit;
        public bool HasSymbol;
        public int Length;

        public override string ToString()
        {
            return Label + " (" + Bits + " bits)";
        }
    }

    /// <summary>Heuristic password strength estimator.</summary>
    internal static class PasswordStrengthEstimator
    {
        private static readonly string[] CommonPasswords =
        {
            "password", "123456", "123456789", "12345678", "12345", "qwerty", "abc123",
            "111111", "123123", "admin", "letmein", "welcome", "monkey", "dragon",
            "iloveyou", "sunshine", "princess", "football", "baseball", "master",
            "shadow", "superman", "trustno1", "hello", "freedom", "whatever",
            "qwertyuiop", "starwars", "passw0rd", "zaq12wsx", "login", "solo"
        };

        internal static PasswordStrength Estimate(string password)
        {
            PasswordStrength result = new PasswordStrength();
            result.Label = "Empty";
            result.Score = 0;
            result.Advice = string.Empty;

            if (string.IsNullOrEmpty(password))
            {
                result.Bits = 0;
                return result;
            }

            result.Length = password.Length;

            int pool = 0;
            bool upper = false, lower = false, digit = false, symbol = false;
            int sequenceRuns = 0;

            for (int i = 0; i < password.Length; i++)
            {
                char c = password[i];
                if (char.IsUpper(c)) { upper = true; pool += 26; }
                else if (char.IsLower(c)) { lower = true; pool += 26; }
                else if (char.IsDigit(c)) { digit = true; pool += 10; }
                else if (c >= 32 && c < 127) { symbol = true; pool += 33; }

                // Count ascending or descending runs of three or more (abc, 987, qwert).
                if (i >= 2)
                {
                    int a = password[i - 2], b = password[i - 1], d = password[i];
                    if (Math.Abs(b - a) == 1 && Math.Abs(d - b) == 1) sequenceRuns++;
                }
            }

            result.HasUppercase = upper;
            result.HasLowercase = lower;
            result.HasDigit = digit;
            result.HasSymbol = symbol;

            if (pool == 0) pool = 1;

            // Raw entropy assuming every character is chosen independently and uniformly.
            int bits = (int)Math.Round(password.Length * (Math.Log(pool) / Math.Log(2.0)));

            // Common-password dictionary: essentially zero guesses for anything on the list.
            string lowered = password.ToLowerInvariant();
            foreach (string common in CommonPasswords)
            {
                if (lowered == common)
                {
                    return new PasswordStrength
                    {
                        Bits = 4,
                        Score = 0,
                        Label = "Very weak",
                        Advice = "This is one of the most common passwords in the world. Any attacker with a word list will try it first.",
                        Length = result.Length,
                        HasUppercase = upper,
                        HasLowercase = lower,
                        HasDigit = digit,
                        HasSymbol = symbol
                    };
                }
            }

            if (lowered.Contains("password") || lowered.Contains("qwerty"))
            {
                bits = Math.Min(bits, 12);
                result.Advice = "Contains a very common word. Attackers try these first.";
            }

            // Repeated characters ("aaaaaa") add far less than the raw count suggests.
            int maxRepeat = LongestRun(password);
            if (maxRepeat >= 3)
            {
                bits -= (maxRepeat - 1) * 3;
                result.Advice = "Long runs of the same character add very little strength.";
            }

            if (sequenceRuns > 0)
            {
                bits -= sequenceRuns * 4;
                result.Advice = "Sequences like 'abc' or '1234' are guessed almost immediately.";
            }

            if (bits < 0) bits = 0;

            result.Bits = bits;

            if (bits < 28) { result.Score = 0; result.Label = "Very weak"; }
            else if (bits < 40) { result.Score = 1; result.Label = "Weak"; }
            else if (bits < 60) { result.Score = 2; result.Label = "Fair"; }
            else if (bits < 80) { result.Score = 3; result.Label = "Strong"; }
            else { result.Score = 4; result.Label = "Excellent"; }

            if (string.IsNullOrEmpty(result.Advice))
            {
                if (bits < 60)
                    result.Advice = "Longer passwords, or generated ones, are dramatically harder to guess.";
                else
                    result.Advice = "Good. Store it in Keyward rather than reusing it elsewhere.";
            }

            return result;
        }

        private static int LongestRun(string s)
        {
            int best = 1, current = 1;
            for (int i = 1; i < s.Length; i++)
            {
                if (s[i] == s[i - 1])
                {
                    current++;
                    if (current > best) best = current;
                }
                else current = 1;
            }
            return best;
        }
    }
}
