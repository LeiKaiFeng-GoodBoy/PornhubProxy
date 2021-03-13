using LeiKaiFeng.Http;
using LeiKaiFeng.Pornhub;
using LeiKaiFeng.X509Certificates;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PornhubProxy
{

    public sealed class Info
    {
        public Info()
        {
            PacServerListen = new IPEndPoint(IPAddress.Any, 1080).ToString();

            ProxyServerListen = new IPEndPoint(IPAddress.Any, 8080).ToString();

            InvalidIPEndPoint = "127.0.0.5:80";

            var ip =
                Dns.GetHostAddresses(Dns.GetHostName())
                .Where((item) => item.AddressFamily == AddressFamily.InterNetwork)
                .Where((item) => item != IPAddress.Loopback)
                .FirstOrDefault() ?? IPAddress.Loopback;


            PacProxyIPEndPoint = new IPEndPoint(ip, IPEndPoint.Parse(ProxyServerListen).Port).ToString();


        }



        public string PacServerListen { get; set; }


        public string ProxyServerListen { get; set; }

        public string PacProxyIPEndPoint { get; set; }

        public string InvalidIPEndPoint { get; set; }
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


    static class PornhubHelper
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


        
    }

    class Program
    {


        static void SetAutoPac(IPEndPoint endPoint)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                
                if (endPoint.Address.ToString() == IPAddress.Any.ToString())
                {
                    endPoint = new IPEndPoint(IPAddress.Loopback, endPoint.Port);
                }

                SetProxy.Set(PacServer.CreatePacUri(endPoint));


            }

        }

        static void Main(string[] args)
        {
            //AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
            //TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            //RuntimeInformation.IsOSPlatform




            string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");

            byte[] vidoBuffer = File.ReadAllBytes(Path.Combine(basePath, "ad.mp4"));
            byte[] caCert = File.ReadAllBytes(Path.Combine(basePath, "myCA.pfx"));
            Info info = JsonSerializer.Deserialize<Info>(File.ReadAllText(Path.Combine(basePath, "info.json"), Encoding.UTF8), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip });


            const string PORNHUB_DNS_HOST = "www.livehub.com";
            
            const string PORNHUB_HOST = "pornhub.com";
            
            const string IWARA_HOST = "iwara.tv";

            const string HENTAI_HOST = "e-hentai.org";

            const string AD_HOST = "adtng.com";


            var pacListensEndPoint = IPEndPoint.Parse(info.PacServerListen);
            var listenEndPoint = IPEndPoint.Parse(info.ProxyServerListen);
            var invalidEndpoint = IPEndPoint.Parse(info.InvalidIPEndPoint);
            var pacWriteEndPoint = IPEndPoint.Parse(info.PacProxyIPEndPoint);
            var adVido = vidoBuffer;




            var ca = new X509Certificate2(caCert);
            var mainCert = TLSCertificate.CreateTlsCertificate(ca, PORNHUB_HOST, 2048, 2, PORNHUB_HOST, "*." + PORNHUB_HOST);
            var adCert = TLSCertificate.CreateTlsCertificate(ca, AD_HOST, 2048, 2, AD_HOST, "*." + AD_HOST);
            var hentaiCert = TLSCertificate.CreateTlsCertificate(ca, HENTAI_HOST, 2048, 2, HENTAI_HOST, "*." + HENTAI_HOST);




            PacServer.Builder.Create(pacListensEndPoint)
                .Add((host) => host == "www.pornhub.com", ProxyMode.CreateHTTP(invalidEndpoint))
                .Add((host) => host == "hubt.pornhub.com", ProxyMode.CreateHTTP(invalidEndpoint))
                .Add((host) => host == "ajax.googleapis.com", ProxyMode.CreateHTTP(invalidEndpoint))
                .Add((host) => PacMethod.dnsDomainIs(host, PORNHUB_HOST), ProxyMode.CreateHTTP(pacWriteEndPoint))
                .Add((host) => PacMethod.dnsDomainIs(host, AD_HOST), ProxyMode.CreateHTTP(pacWriteEndPoint))
                .Add((host) => host == "i.iwara.tv", ProxyMode.CreateDIRECT())
                .Add((host) => PacMethod.dnsDomainIs(host, IWARA_HOST), ProxyMode.CreateHTTP(pacWriteEndPoint))
                .Add((host)=> PacMethod.dnsDomainIs(host, HENTAI_HOST), ProxyMode.CreateHTTP(pacWriteEndPoint))
                .StartPACServer();


            SetAutoPac(pacListensEndPoint);

            PornhubProxyInfo pornhubInfo = new PornhubProxyInfo
            {
                MainPageStreamCreate = ConnectHelper.CreateLocalStream(new X509Certificate2(mainCert), SslProtocols.Tls12),

                ADPageStreamCreate = ConnectHelper.CreateLocalStream(new X509Certificate2(adCert), SslProtocols.Tls12),

                RemoteStreamCreate = ConnectHelper.CreateRemoteStream(PORNHUB_DNS_HOST, 443, PORNHUB_DNS_HOST, (a, b) => new MHttpStream(a, b), SslProtocols.Tls12),

                MaxContentSize = 1024 * 1024 * 5,

                ADVideoBytes = adVido,

                CheckingVideoHtml = PornhubHelper.CheckingVideoHtml,

                MaxRefreshRequestCount = 30,

                ReplaceResponseHtml = PornhubHelper.ReplaceResponseHtml
            };


            var pornhubAction = PornhubProxyServer.Start(pornhubInfo);


            TunnelProxyInfo iwaraSniInfo = new TunnelProxyInfo()
            {
                CreateLocalStream = ConnectHelper.CreateDnsLocalStream(),
                CreateRemoteStream = ConnectHelper.CreateDnsRemoteStream(
                    443,
                    "104.26.12.12",
                    "104.20.201.232",
                    "104.24.48.227",
                    "104.22.27.126",
                    "104.24.53.193")
            };

            var iwaraAction = TunnelProxy.Create(iwaraSniInfo);



            TunnelProxyInfo hentaiInfo = new TunnelProxyInfo()
            { 
                CreateLocalStream = ConnectHelper.CreateLocalStream(hentaiCert, SslProtocols.Tls12),

                CreateRemoteStream = ConnectHelper.CreateRemoteStream("104.20.135.21", 443, "104.20.135.21", (s, ssl) => (Stream)ssl, SslProtocols.Tls12)

            };

            var ehentaiAction = TunnelProxy.Create(hentaiInfo);


            var forw = ForwardTunnelRequest.Builder.Create()
                .Add(IWARA_HOST, iwaraAction)
                .Add(PORNHUB_HOST, pornhubAction)
                .Add(AD_HOST, pornhubAction)
                .Add(HENTAI_HOST, ehentaiAction)
                .Get(listenEndPoint);

            forw.ListenTask.Wait();
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
