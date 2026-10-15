using System;
using System.IO;
using Keyward.Core.Storage;

namespace Keyward.Tests
{
    /// <summary>Tests for the JSON reader/writer and atomic file storage.</summary>
    public static class StorageTests
    {
        public static void Run(string scratchDir)
        {
            Check.Suite("JSON");
            JsonBasics();
            JsonMalformed();

            Check.Suite("Atomic file storage");
            AtomicWrites(scratchDir);
            OversizeGuard(scratchDir);
        }

        private static void JsonBasics()
        {
            Check.Case("parses an object", delegate
            {
                JsonObject o = JsonValue.Parse("{\"a\":1,\"b\":\"two\"}") as JsonObject;
                Check.True(o != null, "did not parse to an object");
                Check.AreEqual(1, o.GetInt("a", 0), "wrong integer");
                Check.AreEqual("two", o.GetString("b", null), "wrong string");
            });

            Check.Case("round-trips nested structures", delegate
            {
                JsonObject root = new JsonObject();
                root.Set("name", "vault");
                JsonArray items = new JsonArray();
                JsonObject child = new JsonObject();
                child.Set("id", 7);
                child.Set("flag", true);
                items.Add(child);
                root.Set("items", items);

                string text = root.ToString();
                JsonObject again = JsonValue.Parse(text) as JsonObject;
                Check.AreEqual("vault", again.GetString("name", null), "string did not survive");
                Check.AreEqual(1, again.GetArray("items").Count, "array length changed");
                JsonObject firstChild = again.GetArray("items")[0] as JsonObject;
                Check.True(firstChild != null, "array element did not parse back to an object");
                Check.AreEqual(7, firstChild.GetInt("id", 0), "nested int changed");
                Check.True(firstChild.GetBool("flag", false), "nested bool changed");
            });

            Check.Case("escapes and unescapes control characters", delegate
            {
                string awkward = "line1\nline2\ttab \"quoted\" back\\slash";
                JsonObject o = new JsonObject();
                o.Set("v", awkward);
                JsonObject parsed = JsonValue.Parse(o.ToString()) as JsonObject;
                Check.AreEqual(awkward, parsed.GetString("v", null), "escaping is not reversible");
            });

            Check.Case("decodes unicode escapes", delegate
            {
                JsonObject o = JsonValue.Parse("{\"v\":\"caf\\u00e9\"}") as JsonObject;
                Check.AreEqual("caf\u00e9", o.GetString("v", null), "\\u escape was not decoded");
            });

            Check.Case("preserves member order", delegate
            {
                JsonObject o = new JsonObject();
                o.Set("z", 1);
                o.Set("a", 2);
                o.Set("m", 3);
                Check.AreEqual("z", o.Keys[0], "insertion order was not preserved");
            });

            Check.Case("pretty printing re-parses", delegate
            {
                JsonObject o = new JsonObject();
                o.Set("a", 1);
                JsonArray arr = new JsonArray();
                arr.Add(new JsonString("x"));
                o.Set("b", arr);
                JsonObject back = JsonValue.Parse(o.ToPrettyString()) as JsonObject;
                Check.AreEqual(1, back.GetInt("a", 0), "pretty output did not round-trip");
            });
        }

        private static void JsonMalformed()
        {
            Check.Case("rejects an unterminated string", delegate
            {
                Check.Throws<JsonParseException>(delegate { JsonValue.Parse("{\"a\":\"unterminated"); },
                    "an unterminated string must be rejected");
            });

            Check.Case("rejects trailing content", delegate
            {
                Check.Throws<JsonParseException>(delegate { JsonValue.Parse("{\"a\":1} garbage"); },
                    "trailing content must be rejected rather than ignored");
            });

            Check.Case("rejects a missing colon", delegate
            {
                Check.Throws<JsonParseException>(delegate { JsonValue.Parse("{\"a\" 1}"); },
                    "a missing colon must be rejected");
            });

            Check.Case("rejects an unescaped control character", delegate
            {
                Check.Throws<JsonParseException>(delegate { JsonValue.Parse("{\"a\":\"x\ny\"}"); },
                    "a raw newline inside a string must be rejected");
            });

            Check.Case("refuses pathological nesting", delegate
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < 500; i++) sb.Append('[');
                Check.Throws<JsonParseException>(delegate { JsonValue.Parse(sb.ToString()); },
                    "deeply nested input must be rejected, not allowed to exhaust the stack");
            });

            Check.Case("refuses a wrong-typed field rather than coercing", delegate
            {
                JsonObject o = JsonValue.Parse("{\"n\":\"not a number\"}") as JsonObject;
                Check.Throws<FormatException>(delegate { o.GetInt("n", 0); },
                    "reading a string as an integer must fail loudly");
            });
        }

        private static void AtomicWrites(string scratchDir)
        {
            string path = Path.Combine(scratchDir, "atomic.bin");

            Check.Case("writes and reads back", delegate
            {
                byte[] payload = System.Text.Encoding.UTF8.GetBytes("first write");
                AtomicFile.WriteAllBytesAtomic(path, payload);
                Check.AreEqual("first write", System.Text.Encoding.UTF8.GetString(AtomicFile.ReadAllBytes(path)),
                    "content did not round-trip");
            });

            Check.Case("replaces an existing file", delegate
            {
                byte[] payload = System.Text.Encoding.UTF8.GetBytes("second write, longer than the first");
                AtomicFile.WriteAllBytesAtomic(path, payload);
                Check.AreEqual("second write, longer than the first",
                    System.Text.Encoding.UTF8.GetString(AtomicFile.ReadAllBytes(path)),
                    "the second write did not replace the first");
            });

            Check.Case("leaves no temp or backup files", delegate
            {
                Check.False(File.Exists(path + ".tmp"), "a .tmp file was left behind");
                Check.False(File.Exists(path + ".bak"), "a .bak file was left behind");
            });

            Check.Case("creates missing directories", delegate
            {
                string nested = Path.Combine(scratchDir, "a", "b", "c", "file.bin");
                AtomicFile.WriteAllBytesAtomic(nested, new byte[] { 1, 2, 3 });
                Check.True(File.Exists(nested), "nested directories were not created");
            });
        }

        private static void OversizeGuard(string scratchDir)
        {
            Check.Case("refuses to load an implausibly large file", delegate
            {
                // Build a sparse file just over the limit without actually writing 64 MB.
                string path = Path.Combine(scratchDir, "huge.bin");
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    fs.SetLength(AtomicFile.MaxVaultBytes + 1);
                }

                Check.Throws<InvalidDataException>(delegate { AtomicFile.ReadAllBytes(path); },
                    "an oversized file must be refused before it is read into memory");
            });
        }
    }
}
