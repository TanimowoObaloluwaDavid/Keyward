using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Keyward.Core.Storage
{
    /// <summary>Raised when a document cannot be parsed as JSON.</summary>
    public class JsonParseException : Exception
    {
        public int Position { get; private set; }

        public JsonParseException(string message, int position)
            : base(message + " (at offset " + position.ToString(CultureInfo.InvariantCulture) + ")")
        {
            Position = position;
        }
    }

    /// <summary>Minimal JSON value model.</summary>
    /// <remarks>
    /// <para>
    /// Keyward targets .NET Framework 4.8, which predates <c>System.Text.Json</c>, and the machine
    /// has no NuGet access, so a small self-contained implementation is used instead of a
    /// third-party serializer. It is deliberately strict: parsing fails loudly rather than
    /// silently coercing unexpected types, because import files and vault files are untrusted
    /// input.
    /// </para>
    /// <para>
    /// Object member order is preserved so round-tripping a file does not reorder it, which
    /// keeps diffs of exported data readable.
    /// </para>
    /// </remarks>
    public abstract class JsonValue
    {
        public virtual bool IsNull { get { return false; } }

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            JsonReader reader = new JsonReader(text);
            JsonValue value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw new JsonParseException("Unexpected trailing content", reader.Position);
            return value;
        }

        public abstract void Write(StringBuilder builder);

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            Write(sb);
            return sb.ToString();
        }

        public string ToPrettyString()
        {
            StringBuilder sb = new StringBuilder();
            WritePretty(sb, 0);
            return sb.ToString();
        }

        internal abstract void WritePretty(StringBuilder builder, int depth);

        protected static void Indent(StringBuilder builder, int depth)
        {
            builder.Append('\n');
            builder.Append(' ', depth * 2);
        }
    }

    public sealed class JsonNull : JsonValue
    {
        public static readonly JsonNull Instance = new JsonNull();
        private JsonNull() { }
        public override bool IsNull { get { return true; } }
        public override void Write(StringBuilder builder) { builder.Append("null"); }
        internal override void WritePretty(StringBuilder builder, int depth) { builder.Append("null"); }
    }

    public sealed class JsonBool : JsonValue
    {
        public bool Value { get; private set; }
        public JsonBool(bool value) { Value = value; }
        public override void Write(StringBuilder builder) { builder.Append(Value ? "true" : "false"); }
        internal override void WritePretty(StringBuilder builder, int depth) { builder.Append(Value ? "true" : "false"); }
    }

    public sealed class JsonNumber : JsonValue
    {
        public double Value { get; private set; }
        public JsonNumber(double value) { Value = value; }

        public override void Write(StringBuilder builder)
        {
            if (double.IsNaN(Value) || double.IsInfinity(Value))
                throw new InvalidOperationException("JSON cannot represent NaN or Infinity.");
            builder.Append(Value.ToString("R", CultureInfo.InvariantCulture));
        }

        internal override void WritePretty(StringBuilder builder, int depth)
        {
            builder.Append(Value.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    public sealed class JsonString : JsonValue
    {
        public string Value { get; private set; }
        public JsonString(string value) { Value = value ?? string.Empty; }

        public override void Write(StringBuilder builder)
        {
            JsonWriter.WriteEscaped(builder, Value);
        }

        internal override void WritePretty(StringBuilder builder, int depth)
        {
            Write(builder);
        }
    }

    public sealed class JsonArray : JsonValue, IEnumerable<JsonValue>
    {
        private readonly List<JsonValue> _items = new List<JsonValue>();

        public JsonArray() { }

        public JsonArray(IEnumerable<JsonValue> items)
        {
            if (items != null) _items.AddRange(items);
        }

        public int Count { get { return _items.Count; } }

        public JsonValue this[int index]
        {
            get { return _items[index]; }
            set { _items[index] = value; }
        }

        public void Add(JsonValue value) { _items.Add(value ?? JsonNull.Instance); }

        public JsonValue[] ToArray() { return _items.ToArray(); }

        public IEnumerator<JsonValue> GetEnumerator() { return _items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return _items.GetEnumerator(); }

        public override void Write(StringBuilder builder)
        {
            builder.Append('[');
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0) builder.Append(',');
                _items[i].Write(builder);
            }
            builder.Append(']');
        }

        internal override void WritePretty(StringBuilder builder, int depth)
        {
            if (_items.Count == 0) { builder.Append("[]"); return; }
            builder.Append('[');
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0) builder.Append(',');
                Indent(builder, depth + 1);
                _items[i].WritePretty(builder, depth + 1);
            }
            Indent(builder, depth);
            builder.Append(']');
        }
    }

    /// <summary>JSON object with insertion-ordered members.</summary>
    public sealed class JsonObject : JsonValue, IEnumerable<KeyValuePair<string, JsonValue>>
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, JsonValue> _members =
            new Dictionary<string, JsonValue>(StringComparer.Ordinal);

        public int Count { get { return _order.Count; } }

        public IReadOnlyList<string> Keys { get { return _order; } }

        public JsonValue this[string key]
        {
            get
            {
                JsonValue value;
                if (!_members.TryGetValue(key, out value)) return null;
                return value;
            }
            set { Set(key, value); }
        }

        public void Set(string key, JsonValue value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty.", "key");
            if (!_members.ContainsKey(key)) _order.Add(key);
            _members[key] = value ?? JsonNull.Instance;
        }

        public void Set(string key, string value) { Set(key, new JsonString(value)); }
        public void Set(string key, bool value) { Set(key, new JsonBool(value)); }
        public void Set(string key, int value) { Set(key, new JsonNumber(value)); }
        public void Set(string key, double value) { Set(key, new JsonNumber(value)); }

        public bool Has(string key) { return _members.ContainsKey(key); }

        public bool TryGetMember(string key, out JsonValue value)
        {
            return _members.TryGetValue(key, out value);
        }

        public bool Remove(string key)
        {
            if (!_members.Remove(key)) return false;
            _order.Remove(key);
            return true;
        }

        // ---- typed accessors that fail loudly rather than coercing -------------------------

        public string GetString(string key, string fallback)
        {
            JsonValue v = this[key];
            if (v == null || v.IsNull) return fallback;
            JsonString s = v as JsonString;
            if (s == null) throw new FormatException("Field '" + key + "' should be a string.");
            return s.Value;
        }

        public string GetRequiredString(string key)
        {
            JsonValue v = this[key];
            JsonString s = v as JsonString;
            if (s == null) throw new FormatException("Field '" + key + "' is required and must be a string.");
            return s.Value;
        }

        public int GetInt(string key, int fallback)
        {
            JsonValue v = this[key];
            if (v == null || v.IsNull) return fallback;
            JsonNumber n = v as JsonNumber;
            if (n == null) throw new FormatException("Field '" + key + "' should be a number.");
            return (int)Math.Round(n.Value, MidpointRounding.AwayFromZero);
        }

        public bool GetBool(string key, bool fallback)
        {
            JsonValue v = this[key];
            if (v == null || v.IsNull) return fallback;
            JsonBool b = v as JsonBool;
            if (b == null) throw new FormatException("Field '" + key + "' should be true or false.");
            return b.Value;
        }

        public JsonArray GetArray(string key)
        {
            JsonValue v = this[key];
            if (v == null || v.IsNull) return null;
            JsonArray a = v as JsonArray;
            if (a == null) throw new FormatException("Field '" + key + "' should be an array.");
            return a;
        }

        public JsonObject GetObject(string key)
        {
            JsonValue v = this[key];
            if (v == null || v.IsNull) return null;
            JsonObject o = v as JsonObject;
            if (o == null) throw new FormatException("Field '" + key + "' should be an object.");
            return o;
        }

        public IEnumerator<KeyValuePair<string, JsonValue>> GetEnumerator()
        {
            foreach (string key in _order)
                yield return new KeyValuePair<string, JsonValue>(key, _members[key]);
        }

        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public override void Write(StringBuilder builder)
        {
            builder.Append('{');
            for (int i = 0; i < _order.Count; i++)
            {
                if (i > 0) builder.Append(',');
                JsonWriter.WriteEscaped(builder, _order[i]);
                builder.Append(':');
                _members[_order[i]].Write(builder);
            }
            builder.Append('}');
        }

        internal override void WritePretty(StringBuilder builder, int depth)
        {
            if (_order.Count == 0) { builder.Append("{}"); return; }
            builder.Append('{');
            for (int i = 0; i < _order.Count; i++)
            {
                if (i > 0) builder.Append(',');
                Indent(builder, depth + 1);
                JsonWriter.WriteEscaped(builder, _order[i]);
                builder.Append(": ");
                _members[_order[i]].WritePretty(builder, depth + 1);
            }
            Indent(builder, depth);
            builder.Append('}');
        }
    }

    internal static class JsonWriter
    {
        public static void WriteEscaped(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\u2028' || c == '\u2029')
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }
    }

    /// <summary>Recursive-descent JSON reader.</summary>
    internal sealed class JsonReader
    {
        private const int MaxDepth = 64;
        private readonly string _text;
        private int _index;
        private int _depth;

        public JsonReader(string text)
        {
            _text = text;
            _index = 0;
            _depth = 0;
        }

        public int Position { get { return _index; } }
        public bool AtEnd { get { return _index >= _text.Length; } }

        public void SkipWhitespace()
        {
            while (_index < _text.Length)
            {
                char c = _text[_index];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _index++;
                else break;
            }
        }

        public JsonValue ReadValue()
        {
            if (_depth > MaxDepth)
                throw new JsonParseException("Document nested too deeply (possible hostile input)", _index);

            SkipWhitespace();
            if (AtEnd) throw new JsonParseException("Unexpected end of document", _index);

            char c = _text[_index];
            switch (c)
            {
                case '{': return ReadObject();
                case '[': return ReadArray();
                case '"': return new JsonString(ReadString());
                case 't': Expect("true"); return new JsonBool(true);
                case 'f': Expect("false"); return new JsonBool(false);
                case 'n': Expect("null"); return JsonNull.Instance;
                default: return ReadNumber();
            }
        }

        private void Expect(string literal)
        {
            if (_index + literal.Length > _text.Length ||
                string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                throw new JsonParseException("Invalid literal", _index);
            _index += literal.Length;
        }

        private JsonObject ReadObject()
        {
            _depth++;
            try
            {
                JsonObject obj = new JsonObject();
                _index++; // '{'
                SkipWhitespace();
                if (!AtEnd && _text[_index] == '}') { _index++; return obj; }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != '"')
                        throw new JsonParseException("Expected a member name", _index);

                    string key = ReadString();

                    SkipWhitespace();
                    if (AtEnd || _text[_index] != ':')
                        throw new JsonParseException("Expected ':' after member name", _index);
                    _index++;

                    JsonValue value = ReadValue();
                    obj.Set(key, value);

                    SkipWhitespace();
                    if (AtEnd) throw new JsonParseException("Unterminated object", _index);

                    char c = _text[_index];
                    if (c == ',') { _index++; continue; }
                    if (c == '}') { _index++; return obj; }

                    throw new JsonParseException("Expected ',' or '}'", _index);
                }
            }
            finally { _depth--; }
        }

        private JsonArray ReadArray()
        {
            _depth++;
            try
            {
                JsonArray array = new JsonArray();
                _index++; // '['
                SkipWhitespace();
                if (!AtEnd && _text[_index] == ']') { _index++; return array; }

                while (true)
                {
                    array.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw new JsonParseException("Unterminated array", _index);

                    char c = _text[_index];
                    if (c == ',') { _index++; continue; }
                    if (c == ']') { _index++; return array; }

                    throw new JsonParseException("Expected ',' or ']'", _index);
                }
            }
            finally { _depth--; }
        }

        private string ReadString()
        {
            _index++; // opening quote
            StringBuilder sb = new StringBuilder();

            while (true)
            {
                if (AtEnd) throw new JsonParseException("Unterminated string", _index);

                char c = _text[_index];

                if (c == '"') { _index++; return sb.ToString(); }

                if (c == '\\')
                {
                    _index++;
                    if (AtEnd) throw new JsonParseException("Unterminated escape sequence", _index);

                    char esc = _text[_index++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _text.Length)
                                throw new JsonParseException("Truncated \\u escape", _index);
                            int code;
                            if (!int.TryParse(_text.Substring(_index, 4), NumberStyles.HexNumber,
                                             CultureInfo.InvariantCulture, out code))
                                throw new JsonParseException("Invalid \\u escape", _index);
                            sb.Append((char)code);
                            _index += 4;
                            break;
                        default:
                            throw new JsonParseException("Unsupported escape sequence '\\" + esc + "'", _index);
                    }
                    continue;
                }

                // Raw control characters are not legal inside a JSON string.
                if (c < ' ')
                    throw new JsonParseException("Unescaped control character in string", _index);

                sb.Append(c);
                _index++;
            }
        }

        private JsonNumber ReadNumber()
        {
            int start = _index;
            if (!AtEnd && (_text[_index] == '-' || _text[_index] == '+')) _index++;

            while (!AtEnd)
            {
                char c = _text[_index];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    _index++;
                else
                    break;
            }

            string slice = _text.Substring(start, _index - start);
            double value;
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new JsonParseException("Invalid number '" + slice + "'", start);

            return new JsonNumber(value);
        }
    }
}
