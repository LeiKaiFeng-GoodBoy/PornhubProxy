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

namespace PornhubProxy
{

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


        public static async Task<MHttpStream> CreateRemoteStream()
        {
          
            const string HOST = "www.livehub.com";
            //const string HOST = "cn.pornhub.com";

            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            await socket.ConnectAsync(HOST, 443).ConfigureAwait(false);

            SslStream sslStream = new SslStream(new NetworkStream(socket, true), false, (a, b, c, d) => true);


            await sslStream.AuthenticateAsClientAsync(HOST, null, System.Security.Authentication.SslProtocols.Tls12, false).ConfigureAwait(false);

            return new MHttpStream(socket, sslStream);
        }

        public static Func<Stream, string, Task<Stream>> CreateLocalStream(X509Certificate certificate)
        {
            return async (stream, host) =>
            {
                SslStream sslStream = new SslStream(stream, false);

                await sslStream.AuthenticateAsServerAsync(certificate, false, System.Security.Authentication.SslProtocols.Tls12, false).ConfigureAwait(false);


                return sslStream;
            };
               
        }
    }


    class Program
    {
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;

            IPEndPoint endPoint = new IPEndPoint(IPAddress.Loopback, 1080);

            PacServer pacServer = PacServer.Start(new IPEndPoint(IPAddress.Loopback, 8080),
                PacServer.Create(endPoint, "cn.pornhub.com", "hw-cdn2.adtng.com", "ht-cdn2.adtng.com", "vz-cdn2.adtng.com"),
                PacServer.Create(new IPEndPoint(IPAddress.Loopback, 80), "www.pornhub.com", "hubt.pornhub.com"));

            SetProxy.Set(pacServer.ProxyUri);

            PornhubProxyInfo info = new PornhubProxyInfo
            {
                MainPageStreamCreate = Connect.CreateLocalStream(new X509Certificate2(File.ReadAllBytes("main.com.pfx"))),

                ADPageStreamCreate = Connect.CreateLocalStream(new X509Certificate2(File.ReadAllBytes("adtng.com.pfx"))),

                RemoteStreamCreate = Connect.CreateRemoteStream,

                MaxContentSize = 1024 * 1024 * 5,

                ADVideoBytes = File.ReadAllBytes("985106_video_with_sound.mp4"),

                CheckingVideoHtml = Connect.CheckingVideoHtml,

                MaxRefreshRequestCount = 30,

                ReplaceResponseHtml = Connect.ReplaceResponseHtml,

            };

            PornhubProxyServer server = new PornhubProxyServer(info);


            Task t = server.Start(endPoint);

            t.Wait();
        }

        private static void CurrentDomain_FirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            string s = Environment.NewLine;
            Console.WriteLine($"{s}{s}{e.Exception}{s}{s}");
        }
    }
}
