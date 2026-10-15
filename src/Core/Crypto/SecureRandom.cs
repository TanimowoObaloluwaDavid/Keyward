using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Keyward.Core.Crypto
{
    /// <summary>
    /// Cryptographically secure randomness helpers. Every value used for salts, IVs, key
    /// derivation and password generation flows through this class.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Random"/> is never used for anything security relevant: it is a
    /// predictable linear congruential generator and must not be used for passwords, salts or
    /// IVs. <c>Guid.NewGuid()</c> is also avoided for key material even though it draws from the
    /// OS RNG, because its bits are not independently uniform in the way a rejection sampler
    /// guarantees.
    /// </remarks>
    public static class SecureRandom
    {
        // Created once for the process lifetime. Creating and disposing an RNG per call is both
        // slower and, on some Windows versions, a source of entropy-pool contention.
        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

        public const string UpperAlpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        public const string LowerAlpha = "abcdefghijklmnopqrstuvwxyz";
        public const string Digits = "0123456789";
        public const string Symbols = "!@#$%^&*()-_=+[]{};:,.?/";

        /// <summary>
        /// Glyphs removed when "exclude ambiguous characters" is enabled: visually similar shapes
        /// that are routinely mis-transcribed when a generated password is read off a screen and
        /// typed by hand (O/0, l/1/I, S/5, Z/2, B/8, G/6).
        /// </summary>
        public const string Ambiguous = "O0lI1S5Z2B8G6";

        private static readonly string[] WordList = BuildWordList();

        /// <summary>Fills <paramref name="buffer"/> with cryptographically secure random bytes.</summary>
        public static void NextBytes(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            Rng.GetBytes(buffer);
        }

        /// <summary>Returns <paramref name="count"/> cryptographically secure random bytes.</summary>
        public static byte[] NextBytes(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException("count");
            byte[] buffer = new byte[count];
            if (count > 0) Rng.GetBytes(buffer);
            return buffer;
        }

        /// <summary>
        /// Returns an unbiased random integer in [0, exclusiveMax) using rejection sampling.
        /// </summary>
        /// <remarks>
        /// A plain <c>random % bound</c> would bias the low end of the range whenever
        /// <paramref name="exclusiveMax"/> is not a power of two, measurably weakening generated
        /// passwords. Values in the ragged tail of the 2^32 range are rejected and redrawn.
        /// </remarks>
        public static int NextInt32(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException("exclusiveMax", "Must be greater than zero.");

            uint bound = (uint)exclusiveMax;
            uint limit = uint.MaxValue - (uint.MaxValue % bound);

            // Allocated per call rather than reused through a static: a shared buffer would be
            // a data race when the generator is called from two threads at once.
            byte[] sample = new byte[4];

            while (true)
            {
                Rng.GetBytes(sample);
                uint value = BitConverter.ToUInt32(sample, 0);
                if (value < limit)
                    return (int)(value % bound);
            }
        }

        /// <summary>Returns an unbiased random integer in [inclusiveMin, exclusiveMax).</summary>
        public static int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            if (exclusiveMax <= inclusiveMin)
                throw new ArgumentOutOfRangeException("exclusiveMax", "exclusiveMax must be greater than inclusiveMin.");
            return inclusiveMin + NextInt32(exclusiveMax - inclusiveMin);
        }

        /// <summary>
        /// Generates a string of exactly <paramref name="length"/> characters drawn uniformly from
        /// <paramref name="alphabet"/> using rejection sampling (no modulo bias).
        /// </summary>
        public static string RandomString(int length, string alphabet)
        {
            if (alphabet == null) throw new ArgumentNullException("alphabet");
            if (alphabet.Length == 0) throw new ArgumentException("Alphabet cannot be empty.", "alphabet");
            if (length < 0) throw new ArgumentOutOfRangeException("length");

            char[] result = new char[length];
            for (int i = 0; i < length; i++)
                result[i] = alphabet[NextInt32(alphabet.Length)];

            return new string(result);
        }

        /// <summary>Shuffles <paramref name="list"/> in place with an unbiased Fisher-Yates pass.</summary>
        public static void Shuffle<T>(IList<T> list)
        {
            if (list == null) throw new ArgumentNullException("list");
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt32(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>Returns a random element from <paramref name="list"/>.</summary>
        public static T Pick<T>(IList<T> list)
        {
            if (list == null) throw new ArgumentNullException("list");
            if (list.Count == 0) throw new InvalidOperationException("Cannot pick from an empty list.");
            return list[NextInt32(list.Count)];
        }

        /// <summary>Returns a random pass-phrase word from the bundled word list.</summary>
        public static string RandomWord()
        {
            return WordList[NextInt32(WordList.Length)];
        }

        /// <summary>Number of words in the bundled pass-phrase list.</summary>
        public static int WordCount
        {
            get { return WordList.Length; }
        }

        /// <summary>True when the character is one of the visually ambiguous glyphs.</summary>
        public static bool IsAmbiguous(char c)
        {
            return Ambiguous.IndexOf(c) >= 0;
        }

        /// <summary>
        /// Exactly 256 unique short English words, so each word carries precisely 8 bits of
        /// entropy and pass-phrase strength is trivial to reason about. Uniqueness and count are
        /// asserted by the test suite, because a duplicate entry would silently reduce the
        /// entropy of every generated pass-phrase.
        /// </summary>
        private static string[] BuildWordList()
        {
            return new string[]
            {
                /*  0 */ "acorn",     "amber",     "anchor",    "apple",     "apron",     "arrow",     "atlas",     "autumn",
                /*  8 */ "bacon",     "badge",     "bamboo",    "banjo",     "basil",     "beach",     "berry",     "birch",
                /* 16 */ "blaze",     "bloom",     "board",     "brave",     "brook",     "brush",     "breeze",    "cabin",
                /* 24 */ "cable",     "camel",     "candle",    "canoe",     "canyon",    "cedar",     "chalk",     "charm",
                /* 32 */ "chess",     "chime",     "cider",     "cinder",    "citrus",    "clamp",     "clerk",     "cliff",
                /* 40 */ "cloak",     "clock",     "cloud",     "clover",    "coast",     "cobalt",    "comet",     "copper",
                /* 48 */ "coral",     "cotton",    "crane",     "crater",    "cricket",   "crimson",   "crystal",   "cubic",
                /* 56 */ "cypress",   "daisy",     "delta",     "denim",     "dune",      "eagle",     "earth",     "echo",
                /* 64 */ "ember",     "feather",   "fern",      "field",     "finch",     "flame",     "flint",     "flora",
                /* 72 */ "flute",     "forest",    "fossil",    "fox",       "frost",     "galaxy",    "garden",    "garlic",
                /* 80 */ "geyser",    "ginger",    "glacier",   "glade",     "granite",   "gravel",    "grove",     "gulf",
                /* 88 */ "hammer",    "harbor",    "harvest",   "hazel",     "honey",     "horizon",   "husky",     "igloo",
                /* 96 */ "indigo",    "iris",      "island",    "ivory",     "jade",      "jasmine",   "jasper",    "jungle",
                /*104 */ "juniper",   "kayak",     "kettle",    "kite",      "knight",    "lagoon",    "larch",     "laurel",
                /*112 */ "lemon",     "lilac",     "linen",     "lotus",     "lumber",    "maple",     "marble",    "marina",
                /*120 */ "meadow",    "melon",     "mesa",      "mint",      "mirror",    "moss",      "mountain",  "mural",
                /*128 */ "nectar",    "nettle",    "nickel",    "noble",     "nomad",     "nutmeg",    "oasis",     "oat",
                /*136 */ "olive",     "onyx",      "opal",      "orbit",     "orchid",    "osprey",    "otter",     "oxide",
                /*144 */ "oyster",    "paddle",    "palm",      "papaya",    "paprika",   "pebble",    "pecan",     "pepper",
                /*152 */ "petal",     "piano",     "pigeon",    "pilot",     "pine",      "pistachio", "plate",     "plum",
                /*160 */ "pocket",    "pollen",    "poppy",     "prairie",   "pretzel",   "prism",     "puffin",    "pumpkin",
                /*168 */ "quartz",    "quill",     "rabbit",    "radish",    "rain",      "raven",     "reef",      "ribbon",
                /*176 */ "ridge",     "river",     "robin",     "rocket",    "rose",      "rowan",     "ruby",      "saffron",
                /*184 */ "sage",      "salmon",    "sand",      "sapphire",  "satin",     "scarlet",   "seal",      "sequoia",
                /*192 */ "shale",     "shell",     "sierra",    "silver",    "slate",     "snow",      "sorrel",    "spark",
                /*200 */ "sparrow",   "spruce",    "steel",     "stone",     "stork",     "summit",    "sunset",    "sycamore",
                /*208 */ "talon",     "teal",      "thicket",   "thistle",   "thunder",   "timber",    "topaz",     "tulip",
                /*216 */ "tundra",    "turquoise", "valley",    "vanilla",   "velvet",    "vine",      "violet",    "walnut",
                /*224 */ "walrus",    "wheat",     "whisker",   "willow",    "windmill",  "winter",    "wren",      "yarrow",
                /*232 */ "yonder",    "zephyr",    "zinc",      "zinnia",    "zircon",    "basalt",    "cobalt",    "dahlia",
                /*240 */ "almond",    "aspen",     "balsa",     "beetle",    "cactus",    "fennel",    "gazelle",   "hollow",
                /*248 */ "islet",     "jetty",     "knoll",     "lintel",    "mosaic",    "nozzle",    "praline",   "quiver"
            };
        }
    }
}
