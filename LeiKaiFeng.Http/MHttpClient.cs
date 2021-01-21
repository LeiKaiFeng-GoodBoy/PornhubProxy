using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;

namespace LeiKaiFeng.Http
{
    [Serializable]
    public sealed class MHttpResponseException : Exception
    {
        public int Status { get; private set; }

        public MHttpResponseException(int status)
        {
            Status = status;
        }
    }

    public sealed class MHttpClient
    {

        public static TimeSpan NeverTimeOutTimeSpan => TimeSpan.FromMilliseconds(-1);


        static void AddTimeOutContinueWith(Task task)
        {
            task.ContinueWith((t) =>
            {
                try
                {
                    t.Wait();
                }
                catch (AggregateException)
                {

                }
                catch (ObjectDisposedException)
                {

                }
            });
        }

        static async Task<T> CreateTimeOutTaskAsync<T>(Task<T> task, CancellationToken cancellationToken, Action cancellationOrExceptionAction, Func<bool> isCancell, Action completedAction)
        {
            T value;
            using (cancellationToken.Register(cancellationOrExceptionAction))
            {
                try
                {
                    value = await task.ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    cancellationOrExceptionAction();

                    if (isCancell())
                    {
                        throw new OperationCanceledException(string.Empty, e, cancellationToken);
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            completedAction();

            return value;
        }

        static void CreateCencellTokenFunc(TimeSpan timeSpan, CancellationToken cancellationToken, out CancellationToken outTokan, out Action outCloseSource)
        {

            if (timeSpan == NeverTimeOutTimeSpan) 
            {
                outTokan = cancellationToken;

                outCloseSource = () => { };
            }
            else
            {
                if (cancellationToken == CancellationToken.None)
                {
                    var timeOutCancellSource = new CancellationTokenSource(timeSpan);

                    outTokan = timeOutCancellSource.Token;

                    outCloseSource = timeOutCancellSource.Dispose;
                }
                else
                {
                    var timeOutCancellSource = new CancellationTokenSource(timeSpan);

                    var joinCancellSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeOutCancellSource.Token);

                    outTokan = joinCancellSource.Token;

                    outCloseSource = () =>
                    {
                        timeOutCancellSource.Dispose();

                        joinCancellSource.Dispose();
                    };
                }
            }
        }

        static void CreateCencellFunc(Action closeSourceAction, Action closeAction, Action completedAction, out Func<bool> outIsCencellFunc, out Action outCompletedAction, out Action outCencellOrExceptionAction)
        {
            int cencellFlag = 0;

            Func<bool> isCencellFunc = () => Volatile.Read(ref cencellFlag) != 0;

            outIsCencellFunc = isCencellFunc;

            outCompletedAction = () =>
            {
                if (isCencellFunc() == false)
                {
                    closeSourceAction();

                    completedAction();
                }
            };

            outCencellOrExceptionAction = () =>
            {
                if (Interlocked.Exchange(ref cencellFlag, 1) == 0)
                {
                    closeAction();
                }
            };

        }

        static Task<T> CreateTimeOutTaskAsync<T>(Task<T> task, TimeSpan timeSpan, CancellationToken cancellationToken, Action closeAction, Action completedAction)
        {
            CreateCencellTokenFunc(
                timeSpan, cancellationToken,
                out CancellationToken outTokan, out Action closeSourceAction);

            CreateCencellFunc(closeSourceAction, closeAction, completedAction,
                out Func<bool> outIsCencellFunc, out Action outCompletedAction, out Action outCencellOrExceptionAction);


            return CreateTimeOutTaskAsync(task, outTokan, outCencellOrExceptionAction, outIsCencellFunc, outCompletedAction);
        }



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

            ResponseTimeOut = NeverTimeOutTimeSpan;

            ConnectTimeOut = NeverTimeOutTimeSpan;
        }


        async Task<MHttpResponse> Internal_SendAsync(MHttpRequest request, MHttpStream stream)
        {         
            await request.SendAsync(stream).ConfigureAwait(false);


            return await MHttpResponse.ReadAsync(stream, m_handler.MaxResponseSize).ConfigureAwait(false);
        }

        async Task<Stream> CreateNewConnectAsync(Socket socket, Uri uri)
        {
            await m_handler.ConnectCallback(socket, uri).ConfigureAwait(false);

            return await m_handler.AuthenticateCallback(new NetworkStream(socket, true), uri).ConfigureAwait(false);
        }

        async Task<MHttpStream> CreateNewConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            Socket socket = new Socket(m_handler.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            var stream = await CreateTimeOutTaskAsync(
                CreateNewConnectAsync(socket, uri),
                ConnectTimeOut,
                cancellationToken,
                socket.Close,
                () => { }).ConfigureAwait(false);

            return new MHttpStream(socket, stream);
        }

        Task<MHttpResponse> Internal_SendAsync(Uri uri, MHttpRequest request, MHttpStream stream, CancellationToken cancellationToken)
        {


            return CreateTimeOutTaskAsync(
                Internal_SendAsync(request, stream),
                ResponseTimeOut,
                cancellationToken,
                stream.Close,
                () => m_pool.Set(uri, stream));
        }


        async Task<MHttpResponse> Internal_SendAsync_Catch_IOException(Uri uri, MHttpRequest request, CancellationToken cancellationToken)
        {
            MHttpStream stream = m_pool.Get(uri);

            if (!(stream is null))
            {
                try
                {
                    return await Internal_SendAsync(uri, request, stream, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {

                }
            }

            stream = await CreateNewConnectAsync(uri, cancellationToken).ConfigureAwait(false);


            return await Internal_SendAsync(uri, request, stream, cancellationToken).ConfigureAwait(false);
        }

        public async Task<MHttpResponse> SendAsync(Uri uri, MHttpRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return await Internal_SendAsync_Catch_IOException(uri, request, cancellationToken).ConfigureAwait(false);
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
                throw new MHttpResponseException(n);
            }
        }

        Task<MHttpResponse> SendGetAsync(Uri uri, CancellationToken cancellationToken)
        {
            MHttpRequest request = MHttpRequest.CreateGet(uri);

            return Internal_SendAsync_Catch_IOException(uri, request, cancellationToken);
        }


        public async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
        {
            try
            {

                MHttpResponse response = await SendGetAsync(uri, cancellationToken).ConfigureAwait(false);

                ChuckResponseStatus(response);


                return response.Content.GetString();
            }
            catch (Exception e)
            {
                throw new MHttpClientException(e);
            }
        }

        public async Task<byte[]> GetByteArrayAsync(Uri uri, Uri referer, CancellationToken cancellationToken)
        {
            try
            {

                MHttpRequest request = MHttpRequest.CreateGet(uri);

                request.Headers.Set("Referer", referer.AbsoluteUri);

                MHttpResponse response = await Internal_SendAsync_Catch_IOException(uri, request, cancellationToken).ConfigureAwait(false);

                ChuckResponseStatus(response);

                return response.Content.GetByteArray();


            }
            catch (Exception e)
            {
                throw new MHttpClientException(e);
            }


        }

        public async Task<byte[]> GetByteArrayAsync(Uri uri, CancellationToken cancellationToken)
        {
            try
            {

                MHttpResponse response = await SendGetAsync(uri, cancellationToken).ConfigureAwait(false);

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