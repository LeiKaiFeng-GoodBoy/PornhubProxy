using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LeiKaiFeng.Http;

namespace Pornhub
{
    sealed class GetPornhubMainHtml
    {

        sealed class RequestPack
        {

            readonly Func<MHttpStream, Task> m_sendRequest;

            readonly TaskCompletionSource<MHttpResponse> m_taskSource;

            public RequestPack(Func<MHttpStream, Task> sendRequest)
            {
                m_sendRequest = sendRequest;

                m_taskSource = new TaskCompletionSource<MHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Task Send(MHttpStream stream)
            {
                return m_sendRequest(stream);
            }

            public Task<MHttpResponse> GetTask()
            {
                return m_taskSource.Task;
            }

            public void SetResult(MHttpResponse response)
            {
                m_taskSource.TrySetResult(response);
            }

            public void SetException(Exception e)
            {
                m_taskSource.TrySetException(e);
            }
        }




        readonly Func<Task<MHttpStream>> m_CreateStream;

        readonly MyChannels<RequestPack> m_channels;

        readonly int m_maxResponseSize;

        public Task Task { get; private set; }

        private GetPornhubMainHtml(Func<Task<MHttpStream>> func, int concurrentConccetCount, int maxResponseSize)
        {
            m_CreateStream = func;

            m_maxResponseSize = maxResponseSize;

            m_channels = new MyChannels<RequestPack>(concurrentConccetCount);
        }

        Func<RequestPack, Task<MHttpResponse>> Create()
        {
            MHttpStream stream = null;

            return async (requestPack) =>
            {
                try
                {
                    if (stream is null)
                    {
                        stream = await m_CreateStream().ConfigureAwait(false);
                    }



                    await requestPack.Send(stream).ConfigureAwait(false);


                    MHttpResponse response = await MHttpResponse.ReadAsync(stream, m_maxResponseSize).ConfigureAwait(false);


                    if (response.Status == 408 || response.Headers.IsClose())
                    {
                        stream.Close();

                        stream = null;

                        return response;
                    }
                    else
                    {
                        return response;
                    }
                }
                catch
                {
                    stream.Close();

                    stream = null;

                    throw;
                }         
            };
        }

        async Task One()
        {
            var sendFunc = Create();
            while (true)
            {
                RequestPack requestPack = await m_channels.ReadAsync().ConfigureAwait(false);

                try
                {
                    MHttpResponse response = await sendFunc(requestPack).ConfigureAwait(false);

                    requestPack.SetResult(response);
                }
                catch (Exception e)
                {
                    requestPack.SetException(e);

                }

            }


        }

        public async Task<MHttpResponse> SendAsync(Func<MHttpStream, Task> func)
        {
            RequestPack pack = new RequestPack(func);

            await m_channels.WriteAsync(pack).ConfigureAwait(false);

            return await pack.GetTask().ConfigureAwait(false);
        }

        void Start(int concurrentConccetCount)
        {
            var list = new List<Task>();

            foreach (var item in Enumerable.Range(0, concurrentConccetCount))
            {
                list.Add(Task.Run(One));
            }

            this.Task = Task.WhenAll(list.ToArray());
        }


        public static GetPornhubMainHtml Create(Func<Task<MHttpStream>> func, int concurrentConccetCount, int maxResponseSize)
        {
            GetPornhubMainHtml request = new GetPornhubMainHtml(func, concurrentConccetCount, maxResponseSize);

            request.Start(concurrentConccetCount);

            return request;
        }

    }
}
