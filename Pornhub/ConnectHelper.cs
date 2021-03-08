using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Collections.Concurrent;


namespace LeiKaiFeng.Pornhub
{
    public static class ConnectHelper
    {
        public static Func<Uri, Task<T>> CreateRemoteStream<T>(string host, int port, string sni, Func<Socket, SslStream, T> func, SslProtocols sslProtocols = SslProtocols.None)
        {
            return async (uri) =>
            {
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                await socket.ConnectAsync(host, port).ConfigureAwait(false);

                SslStream sslStream = new SslStream(new NetworkStream(socket, true), false);


                var info = new SslClientAuthenticationOptions()
                {
                    RemoteCertificateValidationCallback = (a, b, c, d) => true,

                    EnabledSslProtocols = sslProtocols,

                    TargetHost = sni
                };

                await sslStream.AuthenticateAsClientAsync(info, default).ConfigureAwait(false);

                return func(socket, sslStream);
            };



        }

        public static Func<Stream, Uri, Task<Stream>> CreateLocalStream(X509Certificate certificate, SslProtocols sslProtocols = SslProtocols.None)
        {
            return async (stream, host) =>
            {
                SslStream sslStream = new SslStream(stream, false);

                var info = new SslServerAuthenticationOptions()
                {
                    ServerCertificate = certificate,

                    EnabledSslProtocols = sslProtocols
                };

                await sslStream.AuthenticateAsServerAsync(info, default).ConfigureAwait(false);


                return sslStream;
            };

        }



        public static Func<Stream, Uri, Task<Stream>> CreateDnsLocalStream()
        {
            return (stream, host) => Task.FromResult(stream);
        }

        static Func<IPAddress[]> CreateIp(string hostOrIp, params string[] hostOrIps)
        {
            IEnumerable<IPAddress> ips = new IPAddress[] { };

            Array.ForEach(hostOrIps.Append(hostOrIp).ToArray(), (item) =>
            {
                ips = ips.Concat(Dns.GetHostAddresses(item));
            });


            var queue = new ConcurrentQueue<IPAddress>(ips);


            return () =>
            {
                IPAddress ip;

                if (queue.TryDequeue(out ip))
                {
                    queue.Enqueue(ip);
                }

                return queue.ToArray();
            };

        }


        public static Func<Uri, Task<Stream>> CreateDnsRemoteStream(int port, string hostOrIp, params string[] hostOrIps)
        {

            var func = CreateIp(hostOrIp, hostOrIps);
            return async (uri) =>
            {
               
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                await socket.ConnectAsync(func(), port).ConfigureAwait(false);
             
                return new NetworkStream(socket, true);
            };
        }
    }
}