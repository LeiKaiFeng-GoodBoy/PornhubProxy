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

namespace Pornhub
{


    //需要将逐跳消息头去掉，否则两边会有升级协议之类的行为，我在中间变为鸡同鸭讲，也可以建立连接后就只是转发字节，但是因为下面两个原因不得不修改HTML得以加快访问速度
    //所有页面中图片资源与预览视频都在ci，di，ei开头的CDN上，ei访问最快，只需要将HTML中ci，与di替换为ei便可
    //视频页面的视频内容在ev开头的CDN上便会访问很快，但是视频内容不同CDN查询链接不同所以无法简单替换，所以我这里会循环请求以至于出现ev字符串才会返回到客户端代理
    //并且所有观察都基于PC端页面，移动端没有测试，所以强制发送PC端User-Agent
    //综上所述，仅仅简单代理网站主HTML页面，视频，图片内容都可以正常访问

    public sealed class PornhubProxyServer
    {
        readonly Tuple<string, X509Certificate2, Func<MHttpStream, Task>>[] m_tuples;

        readonly GetPornhubMainHtml m_getMainHtml;

        readonly Func<Socket, Tuple<string, X509Certificate2, Func<MHttpStream, Task>>[], Task> m_createLocalConccet;

        readonly int m_maxContectSize;

        readonly int m_maxRefreshRequestCount;

        readonly byte[] m_ad_video_buffer = File.ReadAllBytes("985106_video_with_sound.mp4");

        public PornhubProxyServer(X509Certificate2 pcer, X509Certificate2 adcer, Func<Task<MHttpStream>> createRemoteConccet, Func<Socket, Tuple<string, X509Certificate2, Func<MHttpStream, Task>>[], Task> createLocalConccet, int maxResponseSize, int concurrentConccetCount, int maxRefreshRequestCount)
        {
            m_tuples = new Tuple<string, X509Certificate2, Func<MHttpStream, Task>>[]
            {
                Tuple.Create<string, X509Certificate2, Func<MHttpStream, Task>>("cn.pornhub.com", pcer, MainBodyAsync),

                Tuple.Create<string, X509Certificate2, Func<MHttpStream, Task>>("adtng.com", adcer, AdAsync),
            };

            
            m_getMainHtml = GetPornhubMainHtml.Create(createRemoteConccet, concurrentConccetCount, maxResponseSize);

            m_createLocalConccet = createLocalConccet;


            m_maxContectSize = maxResponseSize;
           

            m_maxRefreshRequestCount = maxRefreshRequestCount;
        }


        static string ReplaceResponseHtml(string html)
        {
            return html;

            //return new StringBuilder(html)
            //    .Replace("ci.", "ei.")
            //    .Replace("di.", "ei.")
            //    .ToString();
        }

        static bool CheckingVideoHtml(string html)
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


        static Func<MHttpStream, Task> CreateSendFunc(MHttpRequest request)
        {
            request.Headers.RemoveHopByHopHeaders();

            request.Headers.SetMyStandardRequestHeaders();


            request.Headers.Set("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/87.0.4280.88 Safari/537.36");

            return request.CreateSendAsync();
        }

        static Task SendResponse(MHttpResponse response, string html, MHttpStream local)
        {
            html = ReplaceResponseHtml(html);

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
                    return await m_getMainHtml.SendAsync(func).ConfigureAwait(false);

                }
                catch (IOException)
                {
                    
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

            Func<Task> createOneRequest = async () =>
            {
                MHttpResponse response = await GetResponseAsync(requset).ConfigureAwait(false);

                string html = response.Content.GetString();

                if (CheckingVideoHtml(html))
                {
                    source.TrySetResult(() => SendResponse(response, html, local));
                }
               
            };

            Func<Task> sendFirstResponse = async () =>
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
            };
            

            foreach (var item in Enumerable.Range(0, m_maxRefreshRequestCount))
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

                response.SetContent(m_ad_video_buffer);

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

                    MHttpRequest request = await MHttpRequest.ReadAsync(localStream, m_maxContectSize).ConfigureAwait(false);

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

        async Task Conccet(Socket socket)
        {

            
            try
            {

                await m_createLocalConccet(socket, m_tuples).ConfigureAwait(false);

                

            }
            catch (Exception e)
            {
                string s = Environment.NewLine;

                //Console.WriteLine($"{s}{s}{e}{s}{s}");
            }
        }

        public Task Add(Socket socket)
        {
            return Task.Run(() => Conccet(socket));
        }
    }
}
