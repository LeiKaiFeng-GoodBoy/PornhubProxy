using System;
using System.Collections.Generic;
using System.Text;

namespace LeiKaiFeng.Http
{
    static class MHttpParse
    {
        static string ParseOneLine(string headers, ref int offset)
        {
            const string c_mark = "\r\n";

            int index = headers.IndexOf(c_mark, offset, StringComparison.OrdinalIgnoreCase);

            if (index == -1)
            {
                throw new FormatException();
            }
            else
            {
                string s = headers.Substring(offset, index - offset);

                offset = index + c_mark.Length;

                return s;
            }

        }

        static KeyValuePair<string, string> ParseKeyValue(string s)
        {
            const string c_mark = ":";

            int index = s.IndexOf(c_mark, StringComparison.OrdinalIgnoreCase);

            if (index == -1)
            {
                throw new FormatException();
            }
            else
            {
                string key = s.Substring(0, index).Trim();

                string value = s.Substring(index + c_mark.Length).Trim();

                return new KeyValuePair<string, string>(key, value);
            }
        }

        public static KeyValuePair<string, MHttpHeaders> ParseLine(ArraySegment<byte> buffer)
        {
            string headers = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);

            int offset = 0;

            string first_line = ParseOneLine(headers, ref offset);


            var dic = new MHttpHeaders();

            while (true)
            {
                string s = ParseOneLine(headers, ref offset);

                if (s.Length == 0)
                {
                    return new KeyValuePair<string, MHttpHeaders>(first_line, dic);
                }
                else
                {
                    var keyValue = ParseKeyValue(s);

                    dic.Set(keyValue.Key, keyValue.Value);
                }

            }


        }


    }
}