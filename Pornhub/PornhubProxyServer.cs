using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using LeiKaiFeng.Http;
using System.Linq;
using System.Threading;
using System.Net;
using System.Threading.Channels;

namespace LeiKaiFeng.Pornhub
{






    //需要将逐跳消息头去掉，否则两边会有升级协议之类的行为，我在中间变为鸡同鸭讲，也可以建立连接后就只是转发字节，但是因为下面两个原因不得不修改HTML得以加快访问速度
    //所有页面中图片资源与预览视频都在ci，di，ei开头的CDN上，ei访问最快，只需要将HTML中ci，与di替换为ei便可
    //视频页面的视频内容在ev开头的CDN上便会访问很快，但是视频内容不同CDN查询链接不同所以无法简单替换，所以我这里会循环请求以至于出现ev字符串才会返回到客户端代理
    //并且所有观察都基于PC端页面，移动端没有测试，所以强制发送PC端User-Agent
    //综上所述，仅仅简单代理网站主HTML页面，视频，图片内容都可以正常访问

    public sealed class PornhubProxyInfo
    {
        public byte[] ADVideoBytes { get; set; }

        public Func<Stream, Uri, Task<Stream>> ADPageStreamCreate { get; set; }

        public Func<Stream, Uri, Task<Stream>> MainPageStreamCreate { get; set; }

        public int MaxContentSize { get; set; }

        public int MaxRefreshRequestCount { get; set; }

        public Func<string, string> ReplaceResponseHtml { get; set; }

        public Func<string, bool> CheckingVideoHtml { get; set; }

        public Func<Uri, Task<MHttpStream>> RemoteStreamCreate { get; set; }
    }

    public sealed class PornhubProxyServer
    {
        const string MAIN_HOST = "cn.pornhub.com";

        const string AD_HOST = "adtng.com";

        

        


        static Func<MHttpStream, Task> CreateSendFunc(MHttpRequest request)
        {
            request.Headers.RemoveHopByHopHeaders();

            request.Headers.SetMyStandardRequestHeaders();


            request.Headers.Set("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.88 Safari/537.36");

            return request.CreateSendAsync();
        }

        Task SendResponse(MHttpResponse response, string html, MHttpStream local)
        {
            html = m_info.ReplaceResponseHtml(html);

            response.Headers.RemoveHopByHopHeaders();

            response.SetContent(html);

            return response.SendAsync(local);
        }




        async Task<MHttpResponse> GetResponseAsync(Func<MHttpStream, Task> func)
        {



            while (true)
            {
                try
                {
                    var pack = new RequestPack(CancellationToken.None, func);

                    await m_getMainHtml.WriteAsync(pack).ConfigureAwait(false);

                    return await pack.Task.ConfigureAwait(false);

                }
                catch (IOException)
                {
                    
                }
                catch (MHttpNotImplementedException)
                {
                    //这个地方主要是因为服务器返回的408响应没有长度,通过断开连接指示长度,相关的逻辑没有写
                }
            }
        }

        async Task GetOneHtmlAsync(Func<MHttpStream, Task> request, MHttpStream local)
        {

            MHttpResponse response = await GetResponseAsync(request).ConfigureAwait(false);

            await SendResponse(response, response.Content.GetString(), local).ConfigureAwait(false);
        }

        Task GetSeleteHtmlAsync(Func<MHttpStream, Task> requset, MHttpStream local)
        {

            var list = new List<Task>();

            var source = new TaskCompletionSource<Func<Task>>();

            async Task createOneRequest()
            {
                MHttpResponse response = await GetResponseAsync(requset).ConfigureAwait(false);

                string html = response.Content.GetString();

                if (m_info.CheckingVideoHtml(html))
                {
                    source.TrySetResult(() => SendResponse(response, html, local));
                }

            }

            async Task sendFirstResponse()
            {
                Task allTask = Task.WhenAll(list.ToArray());

                var task = source.Task;

                Task t = await Task.WhenAny(allTask, task).ConfigureAwait(false);


                if (object.ReferenceEquals(t, task))
                {
                    var v = await task.ConfigureAwait(false);

                    await v().ConfigureAwait(false);
                }
                else
                {
                    await GetOneHtmlAsync(requset, local).ConfigureAwait(false);
                }
            }


            foreach (var item in Enumerable.Range(0, m_info.MaxRefreshRequestCount))
            {
                list.Add(Task.Run(createOneRequest));
            }

            return sendFirstResponse();
        }

        async Task AdAsync(MHttpStream localStream)
        {
            try
            {

                var response = MHttpResponse.Create(200);

                response.SetContent(m_info.ADVideoBytes);

                await response.SendAsync(localStream).ConfigureAwait(false);

                
                
            }
            finally
            {
                localStream?.Close();
            }

        }

        async Task MainBodyAsync(MHttpStream localStream)
        {
            try
            {
                while (true)
                {

                    MHttpRequest request = await MHttpRequest.ReadAsync(localStream, m_info.MaxContentSize).ConfigureAwait(false);

                    var sendFunc = CreateSendFunc(request);

                    if (request.Path.IndexOf("view_video.php?viewkey") != -1)
                    {
                        await GetSeleteHtmlAsync(sendFunc, localStream).ConfigureAwait(false);
                    }
                    else
                    {
                        await GetOneHtmlAsync(sendFunc, localStream).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                localStream?.Close();
            }

            
        }


        Action<Uri, TunnelPack> Connect()
        {
            var func = Conccet_Core();

            return (uri, tunnelPack) => func(uri, tunnelPack);
        }


        Func<Uri, TunnelPack, Task> Conccet_Core()
        {
            return async (uri, tunnelPack) =>
            {
                try
                {
                    
                   
                    string host = uri.Host;

                    if (host.EndsWith(MAIN_HOST))
                    {
                        Stream stream = await m_info.MainPageStreamCreate(tunnelPack.ConnectStream, uri).ConfigureAwait(false);

                        await MainBodyAsync(new MHttpStream(tunnelPack.ConnectSocket, stream)).ConfigureAwait(false);
                    }
                    else if (host.EndsWith(AD_HOST))
                    {
                        Stream stream = await m_info.ADPageStreamCreate(tunnelPack.ConnectStream, uri).ConfigureAwait(false);

                        await AdAsync(new MHttpStream(tunnelPack.ConnectSocket, stream)).ConfigureAwait(false);
                    }
                    else
                    {

                    }

                }
                catch
                {
                }
            };
                    
        }

        public static Action<Uri, TunnelPack> Start(PornhubProxyInfo info)
        {
            
            PornhubProxyServer server = new PornhubProxyServer(info);

            return server.Connect();
        }



        readonly ChannelWriter<RequestPack> m_getMainHtml;

        readonly PornhubProxyInfo m_info;


        private PornhubProxyServer(PornhubProxyInfo info)
        {
            m_info = info;



            m_getMainHtml = MHttpStreamPack.Create(new MHttpClientHandler
            {
                StreamCallback = (uri, handle, token) => m_info.RemoteStreamCreate(uri),

                MaxStreamPoolCount = 6,

                MaxResponseContentSize = m_info.MaxContentSize


            }, new Uri("http://cn.pornhub.com/"));
        }

    }
}
