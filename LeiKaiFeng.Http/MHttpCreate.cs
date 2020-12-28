using System.Collections.Generic;
using System.Text;

namespace LeiKaiFeng.Http
{
    static class MHttpCreate
    {
        public static byte[] EncodeHeaders(string firstLine, Dictionary<string, string> headers)
        {
            const string c_mark = "\r\n";

            StringBuilder sb = new StringBuilder(2048);

            sb.Append(firstLine).Append(c_mark);

            foreach (var item in headers)
            {
                sb.Append(item.Key).Append(": ").Append(item.Value).Append(c_mark);
            }

            sb.Append(c_mark);

            string s = sb.ToString();

            return Encoding.UTF8.GetBytes(s);
        }
    }
}