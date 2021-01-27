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

    static class MyCanell
    {
        sealed class Helper
        {

            int m_cancellFlag = 0;


            CancellationToken m_tokan;

            Action m_closeCencelSourceAction;

            Action m_cencelAction;

            Action m_closeAction;

            Action m_completedAction;

            


            public Helper(TimeSpan timeSpan, CancellationToken cancellationToken, Action cencelAction, Action closeAction, Action completedAction)
            {
                CreateCencellTokenFunc(timeSpan, cancellationToken, out m_tokan, out m_closeCencelSourceAction);

                m_cencelAction = cencelAction;

                m_closeAction = closeAction;

                m_completedAction = completedAction;
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

            bool IsGetLock()
            {
                return Interlocked.Exchange(ref m_cancellFlag, 1) == 0;
            }

            public void CompletedRun()
            {
                if (IsGetLock())
                {
                    m_completedAction();
                }

            }

            public bool ExceptionRun()
            {

                bool b = IsGetLock() == false;

                m_closeAction();

                return b;
            }
            
            public void FinallyRun()
            {
                m_closeCencelSourceAction();
            }


            void Cancel()
            {
                if (IsGetLock())
                {
                    m_cencelAction();
                }     
            }


            public CancellationTokenRegistration Register()
            {
                return m_tokan.Register(Cancel);
            }
        }

        public static TimeSpan NeverTimeOutTimeSpan => TimeSpan.FromMilliseconds(-1);


        
        static async Task<TR> CreateTimeOutTaskAsync<T, TR>(Task<T> task, Func<T, TR> translateFunc, Helper helper)
        {
            T value;
            
            var register = helper.Register();
            
            try
            {
                value = await task.ConfigureAwait(false);      
            }
            catch (Exception e)
            {
               
                if (helper.ExceptionRun())
                {
                    throw new OperationCanceledException(string.Empty, e);
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                register.Dispose();

                helper.FinallyRun();
            }

            //让取消回调注销后再执行

            helper.CompletedRun();

            return translateFunc(value);

        }


        public static Task<TR> CreateTimeOutTaskAsync<T, TR>(Task<T> task, Func<T, TR> translateFunc, TimeSpan timeSpan, CancellationToken cancellationToken, Action cencelAction, Action closeAction, Action completedAction)
        {
            var helper = new Helper(timeSpan, cancellationToken, cencelAction, closeAction, completedAction);

            return CreateTimeOutTaskAsync(task, translateFunc, helper);
        }

    }

    public sealed class MHttpClient
    {


        public static TimeSpan NeverTimeOutTimeSpan => MyCanell.NeverTimeOutTimeSpan;


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

        Task<MHttpStream> CreateNewConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            Socket socket = new Socket(m_handler.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            return MyCanell.CreateTimeOutTaskAsync(
                CreateNewConnectAsync(socket, uri),
                (stream) => new MHttpStream(socket, stream),
                ConnectTimeOut,
                cancellationToken,
                socket.Close,
                socket.Close,
                () => { });
        }

        Task<MHttpResponse> Internal_SendAsync(Uri uri, MHttpRequest request, MHttpStream stream, CancellationToken cancellationToken)
        {


            return MyCanell.CreateTimeOutTaskAsync(
                Internal_SendAsync(request, stream),
                (v) => v,
                ResponseTimeOut,
                cancellationToken,
                stream.Cencel,
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