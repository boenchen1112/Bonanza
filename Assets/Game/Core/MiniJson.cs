// A tiny JSON reader for the hymn tempo maps (Core cannot use UnityEngine.JsonUtility).
// It also accepts // line comments and /* block comments */ and trailing commas, because
// the tempo maps are edited by hand and carry their explanations as comments.
//
// Values: objects are Dictionary<string, object>, arrays are List<object>, numbers are
// double, strings are string, true/false are bool, null is null. Throws FormatException.
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Bonanza.Core
{
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Reader { S = text };
            p.Skip();
            object v = p.Value();
            p.Skip();
            if (p.I < text.Length) throw p.Fail("unexpected text after the value");
            return v;
        }

        sealed class Reader
        {
            public string S;
            public int I;

            public FormatException Fail(string why)
            {
                int line = 1, col = 1;
                for (int k = 0; k < I && k < S.Length; k++) { if (S[k] == '\n') { line++; col = 1; } else col++; }
                return new FormatException("JSON line " + line + ", column " + col + ": " + why);
            }

            public void Skip()
            {
                while (I < S.Length)
                {
                    char c = S[I];
                    if (char.IsWhiteSpace(c) || c == '﻿') I++;
                    else if (c == '/' && I + 1 < S.Length && S[I + 1] == '/') { while (I < S.Length && S[I] != '\n') I++; }
                    else if (c == '/' && I + 1 < S.Length && S[I + 1] == '*')
                    {
                        int end = S.IndexOf("*/", I + 2, StringComparison.Ordinal);
                        if (end < 0) throw Fail("unterminated comment");
                        I = end + 2;
                    }
                    else break;
                }
            }

            public object Value()
            {
                if (I >= S.Length) throw Fail("unexpected end");
                char c = S[I];
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return Str();
                if (c == '-' || (c >= '0' && c <= '9')) return Num();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                throw Fail("unexpected '" + c + "'");
            }

            bool Word(string w)
            {
                if (string.CompareOrdinal(S, I, w, 0, w.Length) != 0) return false;
                I += w.Length;
                return true;
            }

            Dictionary<string, object> Obj()
            {
                var o = new Dictionary<string, object>();
                I++;
                Skip();
                if (I < S.Length && S[I] == '}') { I++; return o; }
                while (true)
                {
                    Skip();
                    if (I < S.Length && S[I] == '}') { I++; return o; }   // trailing comma
                    if (I >= S.Length || S[I] != '"') throw Fail("expected a key");
                    string k = Str();
                    Skip();
                    if (I >= S.Length || S[I] != ':') throw Fail("expected ':'");
                    I++;
                    Skip();
                    o[k] = Value();
                    Skip();
                    if (I < S.Length && S[I] == ',') { I++; continue; }
                    if (I < S.Length && S[I] == '}') { I++; return o; }
                    throw Fail("expected ',' or '}'");
                }
            }

            List<object> Arr()
            {
                var a = new List<object>();
                I++;
                while (true)
                {
                    Skip();
                    if (I < S.Length && S[I] == ']') { I++; return a; }
                    a.Add(Value());
                    Skip();
                    if (I < S.Length && S[I] == ',') { I++; continue; }
                    if (I < S.Length && S[I] == ']') { I++; return a; }
                    throw Fail("expected ',' or ']'");
                }
            }

            string Str()
            {
                var sb = new StringBuilder();
                I++;
                while (true)
                {
                    if (I >= S.Length) throw Fail("unterminated string");
                    char c = S[I++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (I >= S.Length) throw Fail("unterminated string");
                    char e = S[I++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (I + 4 > S.Length) throw Fail("bad \\u escape");
                            sb.Append((char)Convert.ToInt32(S.Substring(I, 4), 16));
                            I += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }

            double Num()
            {
                int s = I;
                if (S[I] == '-') I++;
                while (I < S.Length && (char.IsDigit(S[I]) || S[I] == '.' || S[I] == 'e' || S[I] == 'E' || S[I] == '+' || S[I] == '-')) I++;
                double v;
                if (!double.TryParse(S.Substring(s, I - s), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw Fail("bad number");
                return v;
            }
        }
    }
}
