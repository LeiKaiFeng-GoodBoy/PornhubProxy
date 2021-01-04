using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LeiKaiFeng.Http
{
    public sealed class MHttpClient
    {
        readonly StreamPool m_pool;

        readonly MHttpClientHandler m_handler;

        public TimeSpan ConnectTimeOut { get; set; }



        public TimeSpan ResponseTimeOut { get; set; }

        

        public MHttpClient() : this(new MHttpClientHandler())
        {

        }


        public MHttpClient(MHttpClientHandler handler)
        {
            m_handler = handler;

            m_pool = new StreamPool(handler.MaxStreamPoolCount);

            ResponseTimeOut = new TimeSpan(0, 1, 0);

            ConnectTimeOut = new TimeSpan(0, 0, 9);
        }


        async Task<MHttpResponse> Internal_SendAsync(MHttpRequest request, MHttpStream stream)
        {         
            await request.SendAsync(stream).ConfigureAwait(false);


            return await MHttpResponse.ReadAsync(stream, m_handler.MaxResponseSize).ConfigureAwait(false);
        }

        async Task<Stream> CreateNewConnectAsync(Socket socket, Uri uri)
        {
            await m_handler.ConnectCallback(socket, uri).ConfigureAwait(false);

            return await m_handler.AuthenticateCallback(new NetworkStream(socket, false), uri).ConfigureAwait(false);
        }

        async Task<MHttpStream> CreateNewConnectAsync(Uri uri)
        {
            Socket socket = new Socket(m_handler.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            var stream = await MHttpStream.CreateTimeOutTaskAsync(
                CreateNewConnectAsync(socket, uri),
                ConnectTimeOut,
                socket.Close,
                () => { },
                socket.Close).ConfigureAwait(false);

            return new MHttpStream(socket, stream);
        }

        Task<MHttpResponse> Internal_SendAsync(Uri uri, MHttpRequest request, MHttpStream stream)
        {


            return MHttpStream.CreateTimeOutTaskAsync(
                Internal_SendAsync(request, stream),
                ResponseTimeOut,
                stream.Cancel,
                () => m_pool.Set(uri, stream),
                stream.Close);
        }


        async Task<MHttpResponse> Internal_SendAsync_Catch_IOException(Uri uri, MHttpRequest request)
        {
            MHttpStream stream = m_pool.Get(uri);

            if (!(stream is null))
            {
                try
                {
                    return await Internal_SendAsync(uri, request, stream).ConfigureAwait(false);
                }
                catch (IOException)
                {

                }
            }

            stream = await CreateNewConnectAsync(uri).ConfigureAwait(false);


            return await Internal_SendAsync(uri, request, stream).ConfigureAwait(false);
        }

        public async Task<MHttpResponse> SendAsync(Uri uri, MHttpRequest request)
        {
            try
            {
                return await Internal_SendAsync_Catch_IOException(uri, request).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new MHttpClientException(e);
            }
        }


        static void ChuckResponseStatus(MHttpResponse response)
        {
            int n = response.Status;

            if (n >= 200 && n < 300)
            {

            }
            else
            {
                throw new FormatException($"无效响应{n}");
            }
        }

        Task<MHttpResponse> SendGetAsync(Uri uri)
        {
            MHttpRequest request = MHttpRequest.CreateGet(uri);

            return Internal_SendAsync_Catch_IOException(uri, request);
        }


        public async Task<string> GetStringAsync(Uri uri)
        {
            try
            {

                MHttpResponse response = await SendGetAsync(uri).ConfigureAwait(false);

                ChuckResponseStatus(response);


                return response.Content.GetString();
            }
            catch (Exception e)
            {
                throw new MHttpClientException(e);
            }
        }

        public async Task<byte[]> GetByteArrayAsync(Uri uri, Uri referer)
        {
            try
            {

                MHttpRequest request = MHttpRequest.CreateGet(uri);

                request.Headers.Set("Referer", referer.AbsoluteUri);

                MHttpResponse response = await Internal_SendAsync_Catch_IOException(uri, request).ConfigureAwait(false);

                ChuckResponseStatus(response);

                return response.Content.GetByteArray();


            }
            catch (Exception e)
            {
                throw new MHttpClientException(e);
            }


        }

        public async Task<byte[]> GetByteArrayAsync(Uri uri)
        {
            try
            {

                MHttpResponse response = await SendGetAsync(uri).ConfigureAwait(false);

                ChuckResponseStatus(response);


                return response.Content.GetByteArray();


            }
            catch (Exception e)
            {
                throw new MHttpClientException(e);
            }


        }


    }
}