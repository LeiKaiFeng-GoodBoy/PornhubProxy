using LeiKaiFeng.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TunnelPackAction = System.Action<System.Uri, LeiKaiFeng.Pornhub.TunnelPack>;

namespace LeiKaiFeng.Pornhub
{
    
    public sealed class TunnelPack
    {
        public TunnelPack(Socket connectSocket, Stream connectStream)
        {
            ConnectSocket = connectSocket;
            ConnectStream = connectStream;
        }

        public Socket ConnectSocket { get; }

        public Stream ConnectStream { get; }
    }

    public sealed class ForwardTunnelRequest
    {
        public sealed class Builder
        {
            readonly Dictionary<string, TunnelPackAction> m_dic
                = new Dictionary<string, TunnelPackAction>(StringComparer.OrdinalIgnoreCase);



            private Builder()
            {

            }


            public static Builder Create()
            {
                return new Builder();
            }

            public Builder Add(string host, TunnelPackAction action)
            {
                if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
                {
                    throw new ArgumentException(nameof(host));
                }

                m_dic.Add(host, action);

                return this;
            }

            static IEnumerable<string> CreateHostEnum(string host)
            {
                var vs = host.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);


                foreach (var item in Enumerable.Range(0, vs.Length))
                {
                    yield return string.Join(".", vs.Skip(item));
                }
            }

            public ForwardTunnelRequest Get(IPEndPoint listenPoint)
            {
                return Get((uri, tunnelPack) => tunnelPack.ConnectSocket.Close(), listenPoint);
            }

            public ForwardTunnelRequest Get(TunnelPackAction defaction, IPEndPoint listenPoint)
            {
                return ForwardTunnelRequest.Start((host) =>
                {
                    foreach (var item in CreateHostEnum(host))
                    {
                        if (m_dic.TryGetValue(item, out var action))
                        {
                            return action;
                        }
                    }

                    return defaction;

                }, listenPoint);
            }
        }


        static void ReadConnectRequest(Socket socket, Func<string, TunnelPackAction> func)
        {
            Task.Run(async () =>
            {
                Stream stream = new NetworkStream(socket, true);

                MHttpStream httpStream = new MHttpStream(socket, stream, 1024);


                MHttpRequest request = await MHttpRequest.ReadAsync(httpStream, 1024 * 1024).ConfigureAwait(false);

                MHttpResponse response = MHttpResponse.Create(200);


                await response.SendAsync(httpStream).ConfigureAwait(false);

                Uri uri = new Uri($"http://{request.Path}/");
              
                func(uri.Host)(uri, new TunnelPack(socket, stream));


            });


            
        }



        static ForwardTunnelRequest Start(Func<string, TunnelPackAction> func, IPEndPoint listenPoint)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            socket.Bind(listenPoint);

            socket.Listen(6);


            Task task = Task.Run(async () =>
            {

                try
                {
                    while (true)
                    {

                        var connect = await socket.AcceptAsync().ConfigureAwait(false);


                        ThreadPool.QueueUserWorkItem((obj) => ReadConnectRequest(connect, func));
                        
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }




            });


            return new ForwardTunnelRequest(task, socket);
        }


        private ForwardTunnelRequest(Task task, Socket socket)
        {
            ListenTask = task;

            ListenSocket = socket;
        }


        public Task ListenTask { get; }


        Socket ListenSocket { get; }



        public void Cancel()
        {
            ListenSocket.Close();
        }

    }
}
