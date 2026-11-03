using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GantryTool.Guide
{
    /// <summary>
    /// A small JSON reader and writer for sign designs and themes (the game's .NET 3.5 has none). Objects are
    /// Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            object v = Value(text, ref i);
            Skip(text, ref i);
            if (i != text.Length)
            {
                throw new FormatException("Unexpected text after JSON value at " + i);
            }

            return v;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            Emit(value, sb);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ typed reads (missing or wrong type = default)

        public static string Str(Dictionary<string, object> o, string key, string fallback = null)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v is string ? (string)v : fallback;
        }

        public static float Num(Dictionary<string, object> o, string key, float fallback)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v is double ? (float)(double)v : fallback;
        }

        public static bool Has(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v != null;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> Arr(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) ? v as List<object> : null;
        }

        // ------------------------------------------------------------------ reader

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        private static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length)
            {
                throw new FormatException("Unexpected end of JSON");
            }

            char c = s[i];
            if (c == '{')
            {
                return ReadObject(s, ref i);
            }

            if (c == '[')
            {
                return ReadArray(s, ref i);
            }

            if (c == '"')
            {
                return ReadString(s, ref i);
            }

            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0)
            {
                i += 4;
                return true;
            }

            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0)
            {
                i += 5;
                return false;
            }

            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0)
            {
                i += 4;
                return null;
            }

            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
            {
                i++;
            }

            if (start == i)
            {
                throw new FormatException("Unexpected character '" + c + "' at " + i);
            }

            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var o = new Dictionary<string, object>();
            i++;
            Skip(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return o;
            }

            while (true)
            {
                Skip(s, ref i);
                if (i >= s.Length || s[i] != '"')
                {
                    throw new FormatException("Expected a key at " + i);
                }

                string key = ReadString(s, ref i);
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    throw new FormatException("Expected ':' at " + i);
                }

                i++;
                o[key] = Value(s, ref i);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return o;
                }

                throw new FormatException("Expected ',' or '}' at " + i);
            }
        }

        private static List<object> ReadArray(string s, ref int i)
        {
            var a = new List<object>();
            i++;
            Skip(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return a;
            }

            while (true)
            {
                a.Add(Value(s, ref i));
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return a;
                }

                throw new FormatException("Expected ',' or ']' at " + i);
            }
        }

        private static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i >= s.Length)
                {
                    break;
                }

                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length)
                        {
                            throw new FormatException("Bad \\u escape");
                        }

                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }

            throw new FormatException("Unterminated string");
        }

        // ------------------------------------------------------------------ writer

        private static void Emit(object v, StringBuilder sb)
        {
            if (v == null)
            {
                sb.Append("null");
            }
            else if (v is string)
            {
                EmitString((string)v, sb);
            }
            else if (v is bool)
            {
                sb.Append((bool)v ? "true" : "false");
            }
            else if (v is double || v is float || v is int)
            {
                sb.Append(Convert.ToDouble(v).ToString("R", CultureInfo.InvariantCulture));
            }
            else if (v is Dictionary<string, object>)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)v)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    EmitString(kv.Key, sb);
                    sb.Append(':');
                    Emit(kv.Value, sb);
                }

                sb.Append('}');
            }
            else if (v is List<object>)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in (List<object>)v)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    Emit(item, sb);
                }

                sb.Append(']');
            }
            else
            {
                throw new ArgumentException("Can't write " + v.GetType());
            }
        }

        private static void EmitString(string s, StringBuilder sb)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }
    }
}
