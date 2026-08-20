using System;
using System.Collections.Generic;
using System.Text;

namespace SpawnRowDuel.Rules
{
    /// <summary>Anything wrong with cards.json - malformed text, bad shape, failed validation.</summary>
    public sealed class CardsJsonException : Exception
    {
        public CardsJsonException(string message) : base(message) { }
    }

    public enum JsonType : byte { Null = 0, Bool = 1, Int = 2, String = 3, Array = 4, Object = 5 }

    /// <summary>
    /// A minimal, dependency-free JSON reader for the card registry.
    ///
    /// Hand-written on purpose: UnityEngine.JsonUtility cannot represent the registry's
    /// load-bearing null-vs-0 distinction, and pulling Newtonsoft into the one assembly that must
    /// have zero dependencies would erode the no-Unity, no-package guarantee (design 01 s1.3).
    ///
    /// Numbers are INTEGERS ONLY. The registry contains no fractional values, and the rules core
    /// bans floating point outright - a float sneaking in through card data would be a data bug,
    /// so the parser rejects it loudly instead of quietly rounding.
    ///
    /// Object key order is preserved: registry order is a behavioural contract (pool order feeds
    /// deckOf, commander order feeds the random opponent pick).
    /// </summary>
    public sealed class JsonValue
    {
        public readonly JsonType Type;
        private readonly bool _bool;
        private readonly long _int;
        private readonly string _str;
        private readonly List<JsonValue> _items;                       // Array
        private readonly List<string> _keys;                           // Object - insertion order
        private readonly Dictionary<string, JsonValue> _props;         // Object - ordinal lookup

        private static readonly JsonValue NullValue = new JsonValue();

        private JsonValue() { Type = JsonType.Null; }
        private JsonValue(bool v) { Type = JsonType.Bool; _bool = v; }
        private JsonValue(long v) { Type = JsonType.Int; _int = v; }
        private JsonValue(string v) { Type = JsonType.String; _str = v; }

        private JsonValue(List<JsonValue> items) { Type = JsonType.Array; _items = items; }

        private JsonValue(List<string> keys, Dictionary<string, JsonValue> props)
        {
            Type = JsonType.Object; _keys = keys; _props = props;
        }

        public bool IsNull { get { return Type == JsonType.Null; } }

        public bool AsBool
        {
            get { Require(JsonType.Bool); return _bool; }
        }

        public long AsLong
        {
            get { Require(JsonType.Int); return _int; }
        }

        public int AsInt
        {
            get
            {
                Require(JsonType.Int);
                if (_int < int.MinValue || _int > int.MaxValue)
                    throw new CardsJsonException("integer out of range: " + _int);
                return (int)_int;
            }
        }

        public string AsString
        {
            get { Require(JsonType.String); return _str; }
        }

        public int Count
        {
            get
            {
                if (Type == JsonType.Array) return _items.Count;
                if (Type == JsonType.Object) return _keys.Count;
                throw new CardsJsonException("Count on a " + Type + " value");
            }
        }

        public JsonValue this[int i]
        {
            get { Require(JsonType.Array); return _items[i]; }
        }

        public IReadOnlyList<string> Keys
        {
            get { Require(JsonType.Object); return _keys; }
        }

        /// <summary>Property lookup. Absent and JSON-null both come back as a Null value.</summary>
        public JsonValue Get(string key)
        {
            Require(JsonType.Object);
            JsonValue v;
            return _props.TryGetValue(key, out v) ? v : NullValue;
        }

        // ---- typed helpers with context in the failure message --------------------------------

        public string StrOrNull(string key)
        {
            var v = Get(key);
            return v.IsNull ? null : v.AsString;
        }

        public string StrReq(string key, string ctx)
        {
            var v = Get(key);
            if (v.Type != JsonType.String)
                throw new CardsJsonException(ctx + ": expected string '" + key + "', got " + v.Type);
            return v.AsString;
        }

        public int IntReq(string key, string ctx)
        {
            var v = Get(key);
            if (v.Type != JsonType.Int)
                throw new CardsJsonException(ctx + ": expected integer '" + key + "', got " + v.Type);
            return v.AsInt;
        }

        public int IntOr(string key, int fallback)
        {
            var v = Get(key);
            return v.Type == JsonType.Int ? v.AsInt : fallback;
        }

        public int? IntOrNull(string key)
        {
            var v = Get(key);
            if (v.IsNull) return null;
            return v.AsInt;
        }

        public bool BoolOr(string key, bool fallback)
        {
            var v = Get(key);
            return v.Type == JsonType.Bool ? v.AsBool : fallback;
        }

        public JsonValue ArrReq(string key, string ctx)
        {
            var v = Get(key);
            if (v.Type != JsonType.Array)
                throw new CardsJsonException(ctx + ": expected array '" + key + "', got " + v.Type);
            return v;
        }

        public JsonValue ObjReq(string key, string ctx)
        {
            var v = Get(key);
            if (v.Type != JsonType.Object)
                throw new CardsJsonException(ctx + ": expected object '" + key + "', got " + v.Type);
            return v;
        }

        public string[] StringArray(string key, string ctx)
        {
            var v = Get(key);
            if (v.IsNull) return new string[0];
            if (v.Type != JsonType.Array)
                throw new CardsJsonException(ctx + ": expected array '" + key + "', got " + v.Type);
            var arr = new string[v.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = v[i].AsString;
            return arr;
        }

        private void Require(JsonType t)
        {
            if (Type != t)
                throw new CardsJsonException("expected " + t + ", got " + Type);
        }

        // ---- parser ---------------------------------------------------------------------------

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new CardsJsonException("null JSON input");
            int pos = 0;
            var v = ParseValue(text, ref pos);
            SkipWs(text, ref pos);
            if (pos != text.Length)
                throw new CardsJsonException("trailing content at offset " + pos);
            return v;
        }

        private static JsonValue ParseValue(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (pos >= s.Length) throw new CardsJsonException("unexpected end of input");

            char c = s[pos];
            if (c == '{') return ParseObject(s, ref pos);
            if (c == '[') return ParseArray(s, ref pos);
            if (c == '"') return new JsonValue(ParseString(s, ref pos));
            if (c == 't') { Expect(s, ref pos, "true"); return new JsonValue(true); }
            if (c == 'f') { Expect(s, ref pos, "false"); return new JsonValue(false); }
            if (c == 'n') { Expect(s, ref pos, "null"); return NullValue; }
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref pos);

            throw new CardsJsonException("unexpected character '" + c + "' at offset " + pos);
        }

        private static JsonValue ParseObject(string s, ref int pos)
        {
            pos++;   // consume {
            var keys = new List<string>();
            var props = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

            SkipWs(s, ref pos);
            if (pos < s.Length && s[pos] == '}') { pos++; return new JsonValue(keys, props); }

            while (true)
            {
                SkipWs(s, ref pos);
                string key = ParseString(s, ref pos);
                SkipWs(s, ref pos);
                if (pos >= s.Length || s[pos] != ':')
                    throw new CardsJsonException("expected ':' at offset " + pos);
                pos++;
                var val = ParseValue(s, ref pos);
                if (!props.ContainsKey(key)) keys.Add(key);
                props[key] = val;

                SkipWs(s, ref pos);
                if (pos >= s.Length) throw new CardsJsonException("unterminated object");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == '}') { pos++; return new JsonValue(keys, props); }
                throw new CardsJsonException("expected ',' or '}' at offset " + pos);
            }
        }

        private static JsonValue ParseArray(string s, ref int pos)
        {
            pos++;   // consume [
            var items = new List<JsonValue>();

            SkipWs(s, ref pos);
            if (pos < s.Length && s[pos] == ']') { pos++; return new JsonValue(items); }

            while (true)
            {
                items.Add(ParseValue(s, ref pos));
                SkipWs(s, ref pos);
                if (pos >= s.Length) throw new CardsJsonException("unterminated array");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == ']') { pos++; return new JsonValue(items); }
                throw new CardsJsonException("expected ',' or ']' at offset " + pos);
            }
        }

        private static string ParseString(string s, ref int pos)
        {
            if (pos >= s.Length || s[pos] != '"')
                throw new CardsJsonException("expected string at offset " + pos);
            pos++;

            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= s.Length) throw new CardsJsonException("unterminated string");
                char c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }

                if (pos >= s.Length) throw new CardsJsonException("unterminated escape");
                char e = s[pos++];
                switch (e)
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
                        if (pos + 4 > s.Length) throw new CardsJsonException("truncated \\u escape");
                        int code = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            char h = s[pos++];
                            int d;
                            if (h >= '0' && h <= '9') d = h - '0';
                            else if (h >= 'a' && h <= 'f') d = h - 'a' + 10;
                            else if (h >= 'A' && h <= 'F') d = h - 'A' + 10;
                            else throw new CardsJsonException("bad \\u escape at offset " + pos);
                            code = (code << 4) | d;
                        }
                        sb.Append((char)code);
                        break;
                    default:
                        throw new CardsJsonException("unknown escape '\\" + e + "' at offset " + pos);
                }
            }
        }

        private static JsonValue ParseNumber(string s, ref int pos)
        {
            int start = pos;
            if (s[pos] == '-') pos++;
            while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;

            if (pos < s.Length && (s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E'))
                throw new CardsJsonException(
                    "non-integer number at offset " + start +
                    " - the card registry is integer-only and the rules core bans floats");

            long v;
            if (!long.TryParse(s.Substring(start, pos - start), out v))
                throw new CardsJsonException("malformed number at offset " + start);
            return new JsonValue(v);
        }

        private static void Expect(string s, ref int pos, string word)
        {
            if (pos + word.Length > s.Length || string.CompareOrdinal(s, pos, word, 0, word.Length) != 0)
                throw new CardsJsonException("expected '" + word + "' at offset " + pos);
            pos += word.Length;
        }

        private static void SkipWs(string s, ref int pos)
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') pos++;
                else return;
            }
        }
    }
}
