using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LeiKaiFeng.Http;
using LeiKaiFeng.Pornhub;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using LeiKaiFeng.X509Certificates;

namespace PornhubProxy
{

    public sealed class SniProxyInfo
    {
        public SniProxyInfo(IPEndPoint iPEndPoint, Func<Stream, string, Task<Stream>> createLocalStream, Func<Task<Stream>> createRemoteStream)
        {
            IPEndPoint = iPEndPoint;
            CreateLocalStream = createLocalStream;
            CreateRemoteStream = createRemoteStream;
        }

        public IPEndPoint IPEndPoint { get; }


        public Func<Stream, string, Task<Stream>> CreateLocalStream { get; }

        public Func<Task<Stream>> CreateRemoteStream { get; }

    }

    public sealed class SniProxy
    {
        SniProxyInfo m_info;


        public SniProxy(SniProxyInfo info)
        {
            m_info = info;
        }


        static async Task CatchAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch(Exception e)
            {
                
            }
        }

        async Task Connect(Stream left_stream)
        {
            Stream right_stream;

            left_stream = await LeiKaiFeng.Proxys.ConnectHelper.ReadConnectRequestAsync(left_stream, m_info.CreateLocalStream).Unwrap().ConfigureAwait(false);

            right_stream = await m_info.CreateRemoteStream().ConfigureAwait(false);



            var t1 = left_stream.CopyToAsync(right_stream, 2048);

            var t2 = right_stream.CopyToAsync(left_stream);

            await Task.WhenAny(t1, t2).ConfigureAwait(false);


            left_stream.Close();

            right_stream.Close();

            CatchAsync(t1);

            CatchAsync(t2);
        }

        public Task Start()
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);


            socket.Bind(m_info.IPEndPoint);

            socket.Listen(6);


            return Task.Run(async () =>
            {
                while (true)
                {
                    var connent = await socket.AcceptAsync().ConfigureAwait(false);

                    Task task = Task.Run(() => Connect(new NetworkStream(connent, true)));
                }
            });
        }



    }


    public static class SetProxy
    {

        [DllImport("wininet.dll")]
        static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
        const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        const int INTERNET_OPTION_REFRESH = 37;
     
        static void FlushOs()
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
          
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }

        static RegistryKey OpenKey()
        {
            return Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", true);
        }

        public static void Set(Uri uri)
        {
            RegistryKey registryKey = OpenKey();
            
            registryKey.SetValue("AutoConfigURL", uri.AbsoluteUri);
            //registryKey.SetValue("ProxyEnable", 0);

            FlushOs();
        }

    }


    static class Connect
    {
        public static string ReplaceResponseHtml(string html)
        {
            return html;

            //return new StringBuilder(html)
            //    .Replace("ci.", "ei.")
            //    .Replace("di.", "ei.")
            //    .ToString();
        }
       
        public static bool CheckingVideoHtml(string html)
        {
            if (html.Contains("/ev-h.p") ||
                html.Contains("/ev.p"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }


        public static Func< Task<T>> CreateRemoteStream<T>(string host, int port, string sni, Func<Socket, SslStream, T> func)
        {
            return async () =>
            {
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                await socket.ConnectAsync(host, port).ConfigureAwait(false);

                SslStream sslStream = new SslStream(new NetworkStream(socket, true), false, (a, b, c, d) => true);


                await sslStream.AuthenticateAsClientAsync(sni, null, System.Security.Authentication.SslProtocols.Tls12, false).ConfigureAwait(false);

                return func(socket, sslStream);
            };
           

           
        }

        public static Func<Stream, string, Task<Stream>> CreateLocalStream(X509Certificate certificate)
        {
            return async (stream, host) =>
            {
                SslStream sslStream = new SslStream(stream, false);

                var info = new SslServerAuthenticationOptions()
                {
                    ServerCertificate = certificate,

                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls
                };

                await sslStream.AuthenticateAsServerAsync(info).ConfigureAwait(false);


                return sslStream;
            };
               
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Task.Run(async () =>
            {

                while (true)
                {
                    await Task.Delay(new TimeSpan(0, 0, 10)).ConfigureAwait(false);


                    GC.Collect();
                }

            });

            //AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;

            const string PORNHUB_HOST = "www.livehub.com";
            //const string HOST = "cn.pornhub.com";

            const string IWARA_HOST = "iwara.tv";

            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            var ip = Dns.GetHostAddresses(Dns.GetHostName()).Where((item) => item.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault() ?? IPAddress.Loopback;
            
            IPEndPoint endPoint = new IPEndPoint(ip, 1080);

            IPEndPoint pac = new IPEndPoint(ip, 8080);
            var lpp = new IPEndPoint(IPAddress.Loopback, 80);
            var iwara = new IPEndPoint(ip, 6456);
            PacServer pacServer = PacServer.Start(pac,
                PacHelper.Create((host) => host == "www.pornhub.com", ProxyMode.CreateHTTP(lpp)),
                PacHelper.Create((host) => host == "hubt.pornhub.com", ProxyMode.CreateHTTP(lpp)),
                PacHelper.Create((host) => PacMethod.dnsDomainIs(host, "pornhub.com"), ProxyMode.CreateHTTP(endPoint)),
                PacHelper.Create((host) => PacMethod.dnsDomainIs(host, "adtng.com"), ProxyMode.CreateHTTP(endPoint)),
                PacHelper.Create((host) => PacMethod.dnsDomainIs(host, IWARA_HOST), ProxyMode.CreateHTTP(iwara)));

            
            SetProxy.Set(PacServer.CreatePacUri(pac));

            X509Certificate2 ca = new X509Certificate2("myCA.pfx");

            X509Certificate2 mainCert = TLSCertificate.CreateTlsCertificate("pornhub.com", ca, 2048, 2, "pornhub.com", "*.pornhub.com");
            X509Certificate2 adCert = TLSCertificate.CreateTlsCertificate("adtng.com", ca, 2048, 2, "adtng.com", "*.adtng.com");

            X509Certificate2 iwaraCert = TLSCertificate.CreateTlsCertificate("iwara.tv", ca, 2048, 2, "*.iwara.tv");

            

            PornhubProxyInfo info = new PornhubProxyInfo
            {
                MainPageStreamCreate = Connect.CreateLocalStream(new X509Certificate2(mainCert)),

                ADPageStreamCreate = Connect.CreateLocalStream(new X509Certificate2(adCert)),

                RemoteStreamCreate = Connect.CreateRemoteStream(PORNHUB_HOST, 443, PORNHUB_HOST,(a,b)=> new MHttpStream(a,b)),

                MaxContentSize = 1024 * 1024 * 5,

                ADVideoBytes = File.ReadAllBytes("ad.mp4"),

                CheckingVideoHtml = Connect.CheckingVideoHtml,

                MaxRefreshRequestCount = 30,

                ReplaceResponseHtml = Connect.ReplaceResponseHtml,

            };

            PornhubProxyServer server = new PornhubProxyServer(info);


            Task t = server.Start(endPoint);



            SniProxyInfo sniProxyInfo = new SniProxyInfo(
                iwara,
                Connect.CreateLocalStream(iwaraCert),
                Connect.CreateRemoteStream("104.20.27.25", 443 ,  IWARA_HOST,(a, b) => (Stream)b)
                );


            SniProxy sniProxy = new SniProxy(sniProxyInfo);

            sniProxy.Start();

            Thread.Sleep(-1);
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            string s = Environment.NewLine;
            Console.WriteLine($"{s}{s}{e.Exception}{s}{s}");
        }

        private static void CurrentDomain_FirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            string s = Environment.NewLine;
            Console.WriteLine($"{s}{s}{e.Exception}{s}{s}");
        }
    }
}
