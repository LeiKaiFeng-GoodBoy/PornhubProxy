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


        
        static async Task<TR> CreateTimeOutTaskAsync<T, TR>(Func<Task>[] taskFuncs, Func<Task<T>> resultFunc, Func<T, TR> translateFunc, Helper helper)
        {
            T value;
            
            var register = helper.Register();
            
            try
            {
                foreach (var func in taskFuncs)
                {
                    await func().ConfigureAwait(false);
                }

                value = await resultFunc().ConfigureAwait(false);      
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


        public static Task<TR> CreateTimeOutTaskAsync<T, TR>(Func<Task>[] taskFuncs, Func<Task<T>> resultFunc, Func<T, TR> translateFunc, TimeSpan timeSpan, CancellationToken cancellationToken, Action cencelAction, Action closeAction, Action completedAction)
        {
            var helper = new Helper(timeSpan, cancellationToken, cencelAction, closeAction, completedAction);

            return CreateTimeOutTaskAsync(taskFuncs, resultFunc, translateFunc, helper);
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


        void Internal_SendAsync(MHttpRequest request, MHttpStream stream, out Func<Task>[] taskFuncs, out Func<Task<MHttpResponse>> resultFunc)
        {
            taskFuncs = new Func<Task>[] { () => request.SendAsync(stream) };

            resultFunc = () => MHttpResponse.ReadAsync(stream, m_handler.MaxResponseSize);

        }

        void CreateNewConnectAsync(Socket socket, Uri uri, out Func<Task>[] taskFuncs, out Func<Task<Stream>> resultFunc)
        {
            taskFuncs = new Func<Task>[] { () => m_handler.ConnectCallback(socket, uri) };

            resultFunc = () => m_handler.AuthenticateCallback(new NetworkStream(socket, true), uri);
           
        }

        Task<MHttpStream> CreateNewConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            Socket socket = new Socket(m_handler.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            CreateNewConnectAsync(socket, uri, out var taskFuncs, out var resultFunc);
            
            return MyCanell.CreateTimeOutTaskAsync(
                taskFuncs,
                resultFunc,
                (stream) => new MHttpStream(socket, stream),
                ConnectTimeOut,
                cancellationToken,
                socket.Close,
                socket.Close,
                () => { });
        }

        Task<MHttpResponse> Internal_SendAsync(Uri uri, MHttpRequest request, MHttpStream stream, CancellationToken cancellationToken)
        {
            Internal_SendAsync(request, stream, out var taskFuncs, out var resultFunc);

            return MyCanell.CreateTimeOutTaskAsync(
                taskFuncs,
                resultFunc,
                (v) => v,
                ResponseTimeOut,
                cancellationToken,
                stream.Cencel,
                stream.Close,
                () => m_pool.Set(uri, stream));
        }


        async Task<T> Internal_SendAsync<T>(Uri uri, MHttpRequest request, CancellationToken cancellationToken, Func<MHttpResponse, T> translateFunc)
        {
            try
            {
                MHttpStream stream = m_pool.Get(uri);

                MHttpResponse result;

                if (!(stream is null))
                {
                    try
                    {
                        result = await Internal_SendAsync(uri, request, stream, cancellationToken).ConfigureAwait(false);

                        return translateFunc(result);
                    }
                    catch (IOException)
                    {

                    }
                }

                stream = await CreateNewConnectAsync(uri, cancellationToken).ConfigureAwait(false);


                result = await Internal_SendAsync(uri, request, stream, cancellationToken).ConfigureAwait(false);

                return translateFunc(result);
            }
            catch(Exception e)
            {
                throw new MHttpClientException(e);
            }     
        }

        public Task<MHttpResponse> SendAsync(Uri uri, MHttpRequest request, CancellationToken cancellationToken)
        {
            return Internal_SendAsync(uri, request, cancellationToken, (res) => res);
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

        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
        {
            MHttpRequest request = MHttpRequest.CreateGet(uri);

            return Internal_SendAsync(uri, request, cancellationToken, (response) =>
            {
                ChuckResponseStatus(response);


                return response.Content.GetString();
            });
            
        }

        public Task<byte[]> GetByteArrayAsync(Uri uri, Uri referer, CancellationToken cancellationToken)
        {

            MHttpRequest request = MHttpRequest.CreateGet(uri);

            request.Headers.Set("Referer", referer.AbsoluteUri);

            return Internal_SendAsync(uri, request, cancellationToken, (response) =>
            {



                ChuckResponseStatus(response);

                return response.Content.GetByteArray();
            });

        }

        public Task<byte[]> GetByteArrayAsync(Uri uri, CancellationToken cancellationToken)
        {
            MHttpRequest request = MHttpRequest.CreateGet(uri);

            return Internal_SendAsync(uri, request, cancellationToken, (response) =>
            {
                ChuckResponseStatus(response);


                return response.Content.GetByteArray();
            });

        }


    }
}