using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LeiKaiFeng.Pornhub
{

    public sealed class TunnelProxyInfo
    {
        public IPEndPoint ListenIPEndPoint { get; set; }


        public Func<Stream, Uri, Task<Stream>> CreateLocalStream { get; set; }

        public Func<Uri, Task<Stream>> CreateRemoteStream { get; set; }

        public int BufferSize { get; } = 4096;
    }



    public sealed class TunnelProxy
    {
        static async Task Connect(TunnelProxyInfo info, Stream left_stream)
        {
            Stream right_stream;

            Uri uri = await LeiKaiFeng.Proxys.ProxyRequestHelper.
                ReadConnectRequestAsync(left_stream, (a, b) => b)
                .ConfigureAwait(false);

            left_stream = await info.CreateLocalStream(left_stream, uri).ConfigureAwait(false);

            right_stream = await info.CreateRemoteStream(uri).ConfigureAwait(false);



            var t1 = left_stream.CopyToAsync(right_stream, info.BufferSize);

            var t2 = right_stream.CopyToAsync(left_stream);



            Task t3 = Task.WhenAny(t1, t2).ContinueWith((t) =>
            {

                left_stream.Close();

                right_stream.Close();

            });
        }

        public static TunnelProxy Start(TunnelProxyInfo info)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            socket.Bind(info.ListenIPEndPoint);

            socket.Listen(6);

            Task task = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var connent = await socket.AcceptAsync().ConfigureAwait(false);

                        Task t = Task.Run(() => Connect(info, new NetworkStream(connent, true)));
                    }
                }
                catch (ObjectDisposedException)
                {

                }
            });


            return new TunnelProxy(socket, task);
        }


        private TunnelProxy(Socket listenSocket, Task task)
        {
            ListenSocket = listenSocket;

            Task = task;
        }

        Socket ListenSocket { get; }

        public Task Task { get; }


        public void Cancel()
        {
            ListenSocket.Close();
        }
    }
}
