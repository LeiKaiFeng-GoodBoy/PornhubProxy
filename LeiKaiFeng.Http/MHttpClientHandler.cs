using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LeiKaiFeng.Http
{
    public sealed class MHttpClientHandler
    {

        public static Func<Stream, Uri, Task<Stream>> CreateCreateAuthenticateAsyncFunc(string host)
        {
            return async (stream, uri) =>
            {
                SslStream sslStream = new SslStream(stream, false);

                await sslStream.AuthenticateAsClientAsync(host).ConfigureAwait(false);


                return sslStream;
            };


        }

        public static Func<Socket, Uri, Task> CreateCreateConnectAsyncFunc(string host, int port)
        {
            return (socket, uri) => Task.Run(() => socket.Connect(host, port));
        }

        public Func<Socket, Uri, Task> ConnectCallback { get; set; }

        public Func<Stream, Uri, Task<Stream>> AuthenticateCallback { get; set; }


        public AddressFamily AddressFamily { get; set; }

        public int MaxResponseSize { get; set; }

        public int MaxStreamPoolCount { get; set; }

        public MHttpClientHandler()
        {
            MaxResponseSize = 1024 * 1024 * 10;

            MaxStreamPoolCount = 6;

            AddressFamily = AddressFamily.InterNetwork;

            ConnectCallback = CreateConnectAsync;

            AuthenticateCallback = CreateAuthenticateAsync;
        }

        static Task CreateConnectAsync(Socket socket, Uri uri)
        {

            return socket.ConnectAsync(uri.Host, uri.Port);

        }


        static Task<Stream> CreateHttp(Stream stream, Uri uri)
        {
            return Task.FromResult(stream);
        }

        static async Task<Stream> CreateHttps(Stream stream, Uri uri)
        {
            
            SslStream sslStream = new SslStream(stream, false);

            await sslStream.AuthenticateAsClientAsync(uri.Host).ConfigureAwait(false);

            return sslStream;
        }

        static Task<Stream> CreateAuthenticateAsync(Stream stream, Uri uri)
        {
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                return CreateHttp(stream, uri);
            }
            else if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                return CreateHttps(stream, uri);
            }
            else
            {
                throw new ArgumentException("Uri Scheme");
            }
        }
    }
}