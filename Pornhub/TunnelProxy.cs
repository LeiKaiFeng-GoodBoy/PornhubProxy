using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LeiKaiFeng.Pornhub
{

    public sealed class TunnelProxyInfo
    {
        public Func<Stream, Uri, Task<Stream>> CreateLocalStream { get; set; }

        public Func<Uri, Task<Stream>> CreateRemoteStream { get; set; }

        public int BufferSize { get; } = 4096;
    }



    public sealed class TunnelProxy
    {
        public static Action<Uri, TunnelPack> Create(TunnelProxyInfo info)
        {
            var func = Create_Core(info);


            return (uri, stream) => func(uri, stream);
        }

        static Func<Uri, TunnelPack, Task> Create_Core(TunnelProxyInfo info)
        {
            return async (uri, tunnelPack) =>
            {

               
                Stream left_stream = await info.CreateLocalStream(tunnelPack.ConnectStream, uri).ConfigureAwait(false);

                Stream right_stream = await info.CreateRemoteStream(uri).ConfigureAwait(false);



                var t1 = left_stream.CopyToAsync(right_stream, info.BufferSize);

                var t2 = right_stream.CopyToAsync(left_stream);



                Task t3 = Task.WhenAny(t1, t2).ContinueWith((t) =>
                {

                    left_stream.Close();

                    right_stream.Close();

                });
            };    
        }      
    }
}