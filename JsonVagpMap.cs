using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WpfApp1
{
    internal static class JsonVagpMap
    {
        internal sealed class VagpItem
        {
            public string File;
            public string Name;
            public long Offset;
            public long Length;
            public int? SampleRate;
        }

        internal static List<VagpItem> Load(string jsonPath)
        {
            if (jsonPath == null) throw new ArgumentNullException("jsonPath");
            string json = File.ReadAllText(jsonPath, Encoding.UTF8);

            // Very small JSON reader for the known structure.
            // Expected shape:
            // { "folder":"GData.afs", "files":[ {"file":"x", "vagp":[ {"name":"VAGp1","offset":1,"length":2,"sampleRate":22050}, ... ]}, ... ] }

            var t = new Tokenizer(json);
            t.Expect('{');

            string folder = null;
            var items = new List<VagpItem>();

            while (!t.TryConsume('}'))
            {
                string prop = t.ReadString();
                t.Expect(':');

                if (prop == "folder")
                {
                    folder = t.ReadString();
                }
                else if (prop == "files")
                {
                    t.Expect('[');
                    if (!t.TryConsume(']'))
                    {
                        do
                        {
                            t.Expect('{');
                            string file = null;
                            while (!t.TryConsume('}'))
                            {
                                string fprop = t.ReadString();
                                t.Expect(':');

                                if (fprop == "file")
                                {
                                    file = t.ReadString();
                                }
                                else if (fprop == "vagp")
                                {
                                    t.Expect('[');
                                    if (!t.TryConsume(']'))
                                    {
                                        do
                                        {
                                            t.Expect('{');

                                            string name = null;
                                            long offset = 0;
                                            long length = 0;
                                            int? sampleRate = null;

                                            while (!t.TryConsume('}'))
                                            {
                                                string vprop = t.ReadString();
                                                t.Expect(':');
                                                if (vprop == "name") name = t.ReadString();
                                                else if (vprop == "offset") offset = t.ReadInt64();
                                                else if (vprop == "length") length = t.ReadInt64();
                                                else if (vprop == "sampleRate") sampleRate = t.ReadNullableInt32();
                                                else t.SkipValue();

                                                t.TryConsume(',');
                                            }

                                            if (file != null && name != null && length > 0)
                                            {
                                                items.Add(new VagpItem
                                                {
                                                    File = file,
                                                    Name = name,
                                                    Offset = offset,
                                                    Length = length,
                                                    SampleRate = sampleRate
                                                });
                                            }

                                        } while (t.TryConsume(','));

                                        t.Expect(']');
                                    }
                                }
                                else
                                {
                                    t.SkipValue();
                                }

                                t.TryConsume(',');
                            }
                        } while (t.TryConsume(','));

                        t.Expect(']');
                    }
                }
                else
                {
                    t.SkipValue();
                }

                t.TryConsume(',');
            }

            // folder currently unused; json holds relative file paths.
            return items;
        }

        private sealed class Tokenizer
        {
            private readonly string _s;
            private int _i;

            public Tokenizer(string s)
            {
                _s = s ?? string.Empty;
                _i = 0;
            }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') _i++;
                    else break;
                }
            }

            public void Expect(char ch)
            {
                SkipWhitespace();
                if (_i >= _s.Length || _s[_i] != ch)
                    throw new FormatException("JSON inválido. Esperado: '" + ch + "' em " + _i);
                _i++;
            }

            public bool TryConsume(char ch)
            {
                SkipWhitespace();
                if (_i < _s.Length && _s[_i] == ch)
                {
                    _i++;
                    return true;
                }
                return false;
            }

            public string ReadString()
            {
                SkipWhitespace();
                Expect('"');
                var sb = new StringBuilder();
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '"')
                        break;
                    if (c == '\\')
                    {
                        if (_i >= _s.Length) throw new FormatException("JSON inválido");
                        char e = _s[_i++];
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
                                if (_i + 4 > _s.Length) throw new FormatException("JSON inválido");
                                string hex = _s.Substring(_i, 4);
                                _i += 4;
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                break;
                            default:
                                throw new FormatException("Escape inválido: \\" + e);
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }

            public long ReadInt64()
            {
                SkipWhitespace();
                int start = _i;
                if (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+')) _i++;

                bool any = false;
                while (_i < _s.Length && char.IsDigit(_s[_i]))
                {
                    any = true;
                    _i++;
                }

                // Defensive: some writers may output 123.0; accept and ignore fractional part.
                if (_i < _s.Length && _s[_i] == '.')
                {
                    _i++;
                    while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
                }

                if (!any) throw new FormatException("Número inválido");

                string n = _s.Substring(start, _i - start);
                int dot = n.IndexOf('.');
                if (dot >= 0) n = n.Substring(0, dot);

                return long.Parse(n);
            }

            public int ReadInt32()
            {
                return (int)ReadInt64();
            }

            public int? ReadNullableInt32()
            {
                SkipWhitespace();
                if (_i + 4 <= _s.Length && string.Compare(_s, _i, "null", 0, 4, StringComparison.Ordinal) == 0)
                {
                    _i += 4;
                    return null;
                }
                return ReadInt32();
            }

            public void SkipValue()
            {
                SkipWhitespace();
                if (_i >= _s.Length) return;
                char c = _s[_i];
                if (c == '"')
                {
                    ReadString();
                    return;
                }
                if (c == '{')
                {
                    Expect('{');
                    while (!TryConsume('}'))
                    {
                        ReadString();
                        Expect(':');
                        SkipValue();
                        TryConsume(',');
                    }
                    return;
                }
                if (c == '[')
                {
                    Expect('[');
                    while (!TryConsume(']'))
                    {
                        SkipValue();
                        TryConsume(',');
                    }
                    return;
                }

                // number, true, false, null
                while (_i < _s.Length)
                {
                    c = _s[_i];
                    if (c == ',' || c == '}' || c == ']' || c == ' ' || c == '\t' || c == '\r' || c == '\n')
                        break;
                    _i++;
                }
            }
        }
    }
}
