// 极简 JSON 解析/序列化（仅依赖 BCL，AOT 安全，无外部依赖）。
// 用于远程向量库（Qdrant / pgvector）REST 响应解析与本地哈希持久化。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class RagMiniJson
{
    public static object Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        int idx = 0;
        SkipWhitespace(json, ref idx);
        return ParseValue(json, ref idx);
    }

    static void SkipWhitespace(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    static object ParseValue(string s, ref int i)
    {
        SkipWhitespace(s, ref i);
        if (i >= s.Length) return null;
        char c = s[i];
        if (c == '{') return ParseObject(s, ref i);
        if (c == '[') return ParseArray(s, ref i);
        if (c == '"') return ParseString(s, ref i);
        if (c == 't' || c == 'f') return ParseBool(s, ref i);
        if (c == 'n') { i += 4; return null; }
        return ParseNumber(s, ref i);
    }

    static Dictionary<string, object> ParseObject(string s, ref int i)
    {
        var d = new Dictionary<string, object>();
        i++; // {
        SkipWhitespace(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return d; }
        while (i < s.Length)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) break;
            string key = ParseString(s, ref i);
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ':') i++;
            object val = ParseValue(s, ref i);
            d[key] = val;
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; break; }
        }
        return d;
    }

    static List<object> ParseArray(string s, ref int i)
    {
        var list = new List<object>();
        i++; // [
        SkipWhitespace(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return list; }
        while (i < s.Length)
        {
            object val = ParseValue(s, ref i);
            list.Add(val);
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; break; }
        }
        return list;
    }

    static string ParseString(string s, ref int i)
    {
        i++; // opening quote
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '\\')
            {
                char e = s[i++];
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
                        string hex = s.Substring(i, 4); i += 4;
                        sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                        break;
                    default: sb.Append(e); break;
                }
            }
            else if (c == '"') break;
            else sb.Append(c);
        }
        return sb.ToString();
    }

    static object ParseNumber(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
        string num = s.Substring(start, i - start);
        return double.Parse(num, CultureInfo.InvariantCulture);
    }

    static bool ParseBool(string s, ref int i)
    {
        if (s[i] == 't') { i += 4; return true; }
        i += 5; return false;
    }

    // ---- Serialize ----
    public static string Str(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder();
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
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    public static string Serialize(object obj)
    {
        var sb = new StringBuilder();
        SerializeValue(obj, sb);
        return sb.ToString();
    }

    static void SerializeValue(object obj, StringBuilder sb)
    {
        if (obj == null) sb.Append("null");
        else if (obj is string str) sb.Append(Str(str));
        else if (obj is bool b) sb.Append(b ? "true" : "false");
        else if (obj is double d) sb.Append(d.ToString(CultureInfo.InvariantCulture));
        else if (obj is int ii) sb.Append(ii.ToString(CultureInfo.InvariantCulture));
        else if (obj is Dictionary<string, object> dct)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in dct) { if (!first) sb.Append(','); first = false; sb.Append(Str(kv.Key)).Append(':'); SerializeValue(kv.Value, sb); }
            sb.Append('}');
        }
        else if (obj is List<object> list)
        {
            sb.Append('[');
            bool first = true;
            foreach (var it in list) { if (!first) sb.Append(','); first = false; SerializeValue(it, sb); }
            sb.Append(']');
        }
        else sb.Append(Str(obj.ToString()));
    }
}
