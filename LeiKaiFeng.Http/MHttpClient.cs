using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Channels;

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

    

    sealed class MHttpStreamPack
    {
        sealed class ResponsePack
        {
            readonly TaskCompletionSource<MHttpResponse> m_source = new TaskCompletionSource<MHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            public ResponsePack(CancellationToken token, TimeSpan timeSpan, int maxResponseSize)
            {
                Token = token;
            
                TimeSpan = timeSpan;
                
                MaxResponseSize = maxResponseSize;
            }

            public CancellationToken Token { get; private set; }

            public TimeSpan TimeSpan { get; private set; }

            public int MaxResponseSize { get; private set; }

            public Task<MHttpResponse> Task => m_source.Task;

            public void Send(Exception e)
            {
                m_source.TrySetException(e);
            }

            public void Send(MHttpResponse response)
            {
                m_source.TrySetResult(response);
            }
        }

        readonly ChannelReader<ResponsePack> m_channelReader;

        readonly ChannelWriter<ResponsePack> m_channelWriter;

        readonly MHttpStream m_stream;

        int m_count;

        public MHttpStreamPack(MHttpStream stream, int maxRequestCount)
        {
            m_stream = stream;

            var channel = Channel.CreateBounded<ResponsePack>(maxRequestCount);

            m_channelReader = channel;

            m_channelWriter = channel;

            m_count = 0;
        }

        public Task<MHttpResponse> SendAsync(Func<MHttpStream, Task> sendRequestFunc, Func<MHttpStreamPack, bool> setPoolFunc, TimeSpan timeSpan, CancellationToken token, int maxResponseSize)
        {
            return SendAsync(sendRequestFunc, setPoolFunc, new ResponsePack(token, timeSpan, maxResponseSize));
        }

        async Task<MHttpResponse> SendAsync(Func<MHttpStream, Task> sendRequestFunc, Func<MHttpStreamPack,bool> setPoolFunc, ResponsePack taskPack)
        {
            await sendRequestFunc(m_stream).ConfigureAwait(false);

            await m_channelWriter.WriteAsync(taskPack).ConfigureAwait(false);

            ReadResponse();

            setPoolFunc(this);

            return await taskPack.Task.ConfigureAwait(false);
        }



        void ReadResponse()
        {
            if (Interlocked.Increment(ref m_count) == 1)
            {
                ThreadPool.QueueUserWorkItem((obj) => ReadResponseAsync());
            }
        }

        

        //这个地方的主要功能在于让读取一个一个的进行,不能并行读取
        async Task ReadResponseAsync()
        {
            
            do
            {
                if (!m_channelReader.TryRead(out var taskPack))
                {
                    throw new NotImplementedException("内部出错");
                }
                else
                {

                    
                    MHttpClient.LinkedTimeOutAndCancel(taskPack.TimeSpan, taskPack.Token, m_stream.Cencel, out var token, out var closeAction);

                    Action action;

                    try
                    {
                        MHttpResponse response = await MHttpResponse.ReadAsync(m_stream, taskPack.MaxResponseSize).ConfigureAwait(false);

                        action = () => taskPack.Send(response);
                    }
                    catch(Exception e)
                    {
                        if (token.IsCancellationRequested)
                        {
                            action = () => taskPack.Send(new OperationCanceledException(string.Empty, e));
                        }
                        else
                        {
                            action = () => taskPack.Send(e);
                        }
                    }

                    closeAction();

                    action();
                }
            }
            while (Interlocked.Decrement(ref m_count) != 0);
        }
    }

    public sealed class MHttpClient
    {
        internal static void LinkedTimeOutAndCancel(TimeSpan timeOutSpan, CancellationToken token, Action cancelAction, out CancellationToken outToken, out Action closeAction)
        {


            if (timeOutSpan == MHttpClient.NeverTimeOutTimeSpan)
            {
                if (token == CancellationToken.None)
                {
                    outToken = token;

                    closeAction = () => { };
                }
                else
                {
                    outToken = token;

                    var register = outToken.Register(cancelAction);

                    closeAction = () => register.Dispose();
                }
            }
            else
            {
                if (token == CancellationToken.None)
                {
                    var source = new CancellationTokenSource(timeOutSpan);

                    outToken = source.Token;

                    var resgister = outToken.Register(cancelAction);

                    closeAction = () =>
                    {
                        resgister.Dispose();

                        source.Dispose();
                    };
                }
                else
                {
                    var source = new CancellationTokenSource(timeOutSpan);


                    var register_0 = token.Register(source.Cancel);

                    outToken = source.Token;

                    var register_1 = outToken.Register(cancelAction);

                    closeAction = () =>
                    {
                        register_1.Dispose();

                        register_0.Dispose();

                        source.Dispose();
                    };

                }
            }
        }

        public static Task<TR> TimeOutAndCancelAsync<T, TR>(Task<T> task, Func<T, TR> translateFunc, Action cancelAction,TimeSpan timeOutSpan, CancellationToken token)
        {
            LinkedTimeOutAndCancel(timeOutSpan, token, cancelAction, out var outToken, out var closeAction);
            
            async Task<TR> F()
            {
                try
                {
                    T v = await task.ConfigureAwait(false);

                    return translateFunc(v);
                }
                catch(Exception e)
                {
                    if (outToken.IsCancellationRequested)
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
                    closeAction();
                }
            }


            return F();
        }


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

        void CreateNewConnectAsync(Socket socket, Uri uri, out Func<Task>[] taskFuncs, out Func<Task<Stream>> resultFunc)
        {
            taskFuncs = new Func<Task>[] { () => m_handler.ConnectCallback(socket, uri) };

            resultFunc = () => m_handler.AuthenticateCallback(new NetworkStream(socket, true), uri);
           
        }

        Task<MHttpStreamPack> CreateNewConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            Socket socket = new Socket(m_handler.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            CreateNewConnectAsync(socket, uri, out var taskFuncs, out var resultFunc);

            return MyCanell.CreateTimeOutTaskAsync(
                taskFuncs,
                resultFunc,
                (stream) => new MHttpStreamPack(new MHttpStream(socket, stream), m_handler.MaxOneStreamRequestCount),
                ConnectTimeOut,
                cancellationToken,
                socket.Close,
                socket.Close,
                () => { });
        }


        async Task<T> Internal_SendAsync<T>(Uri uri, MHttpRequest request, CancellationToken cancellationToken, Func<MHttpResponse, T> translateFunc)
        {
            try
            {
                MHttpStreamPack pack = default;

                var requsetFunc = request.CreateSendAsync();
                
                Task<MHttpResponse> func() => pack.SendAsync(requsetFunc, (item) => m_pool.Set(uri, item), ConnectTimeOut, cancellationToken, m_handler.MaxResponseSize);

                while (m_pool.Get(uri, out pack))
                {
                  
                    try
                    {
                        return translateFunc(await func().ConfigureAwait(false));

                    }
                    catch (IOException)
                    {
                    }
                    catch(ObjectDisposedException)
                    {
                    }
                }

                pack = await CreateNewConnectAsync(uri, cancellationToken).ConfigureAwait(false);


                return translateFunc(await func().ConfigureAwait(false));

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