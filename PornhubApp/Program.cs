﻿using System;
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
using System.Security.Authentication;
using System.Net.NetworkInformation;
using LeiKaiFeng.Proxys;

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
        static void Main(string[] args)
        {
            //AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
            //TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            //RuntimeInformation.IsOSPlatform

            string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");

            byte[] vidoBuffer = File.ReadAllBytes(Path.Combine(basePath, "ad.mp4"));
            byte[] caCert = File.ReadAllBytes(Path.Combine(basePath, "myCA.pfx"));

            const string PORNHUB_DNS_HOST = "www.livehub.com";
            
            const string PORNHUB_HOST = "pornhub.com";
            
            const string IWARA_HOST = "iwara.tv";

            const string HENTAI_HOST = "e-hentai.org";

            const string AD_HOST = "adtng.com";



            var ip = Dns.GetHostAddresses(Dns.GetHostName()).Where((item) => item.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault() ?? IPAddress.Loopback;
            
            
            var pacListensEndPoint = new IPEndPoint(ip, 1080);
            var listenEndPoint = new IPEndPoint(ip, 8080);
            var adErrorEndpoint = new IPEndPoint(IPAddress.Loopback, 80);
            var adVido = vidoBuffer;






            var ca = CaPack.Create(caCert);
            var mainCert = TLSBouncyCastleHelper.GenerateTls(ca, PORNHUB_HOST, 2048, 2, PORNHUB_HOST, "*." + PORNHUB_HOST);
            var adCert = TLSBouncyCastleHelper.GenerateTls(ca, AD_HOST, 2048, 2, AD_HOST, "*." + AD_HOST);
            var hentaiCert = TLSBouncyCastleHelper.GenerateTls(ca, HENTAI_HOST, 2048, 2, HENTAI_HOST, "*." + HENTAI_HOST);

            SetProxy.Set(PacServer.CreatePacUri(pacListensEndPoint));

            PacServer.Builder.Create(pacListensEndPoint)
                .Add((host) => host == "www.pornhub.com", ProxyMode.CreateHTTP(adErrorEndpoint))
                .Add((host) => host == "hubt.pornhub.com", ProxyMode.CreateHTTP(adErrorEndpoint))
                .Add((host) => host == "ajax.googleapis.com", ProxyMode.CreateHTTP(adErrorEndpoint))
                .Add((host) => PacMethod.dnsDomainIs(host, PORNHUB_HOST), ProxyMode.CreateHTTP(listenEndPoint))
                .Add((host) => PacMethod.dnsDomainIs(host, AD_HOST), ProxyMode.CreateHTTP(listenEndPoint))
                .Add((host) => host == "i.iwara.tv", ProxyMode.CreateDIRECT())
                .Add((host) => PacMethod.dnsDomainIs(host, IWARA_HOST), ProxyMode.CreateHTTP(listenEndPoint))
                .Add((host)=> PacMethod.dnsDomainIs(host, HENTAI_HOST), ProxyMode.CreateHTTP(listenEndPoint))
                .StartPACServer();

            PornhubProxyInfo info = new PornhubProxyInfo
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


            var pornhubAction = PornhubProxyServer.Start(info);


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
