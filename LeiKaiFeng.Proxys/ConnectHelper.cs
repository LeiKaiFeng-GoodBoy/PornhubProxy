using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LeiKaiFeng.Proxys
{
    public static class ConnectHelper
    {


        static string GetHost(byte[] buffer, int offset, int count)
        {
            string s = Encoding.UTF8.GetString(buffer, offset, count);

            return 
                s.Split(new string[] { "\r\n" }, 2, StringSplitOptions.RemoveEmptyEntries)[0]
                .Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries)[1]
                .Split(new char[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        }

        static Task SendOKAsync(Stream stream)
        {
            byte[] buffer = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\n\r\n");

            return stream.WriteAsync(buffer, 0, buffer.Length);
        }

        public static async Task<T> ReadConnectRequestAsync<T>(Stream stream, Func<Stream, string, T> func)
        {
            byte[] buffer = new byte[1024];

            int count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

            await SendOKAsync(stream).ConfigureAwait(false);

            return func(stream, GetHost(buffer, 0, count));
        }

    }
}
