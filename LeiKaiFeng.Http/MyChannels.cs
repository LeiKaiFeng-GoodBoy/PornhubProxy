using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LeiKaiFeng.Http
{
    //完成后读取端可能会丢失项

    [Serializable]
    public sealed class MyChannelsCompletedException : Exception
    {

    }

    public sealed class MyChannels<T>
    {
        

        readonly CancellationTokenSource m_source = new CancellationTokenSource();

        readonly ConcurrentQueue<T> m_queue = new ConcurrentQueue<T>();

        readonly SemaphoreSlim m_write_slim;

        readonly SemaphoreSlim m_read_slim;

        public CancellationToken CancellationToken => m_source.Token;

        public bool IsComplete => m_source.IsCancellationRequested;

        public MyChannels(int maxCount)
        {
            m_write_slim = new SemaphoreSlim(maxCount, maxCount);

            m_read_slim = new SemaphoreSlim(0, maxCount);
        }

        async Task WriteAsync_(T value)
        {
            try
            {

                await m_write_slim.WaitAsync(m_source.Token).ConfigureAwait(false);

            }
            catch (OperationCanceledException)
            {
                throw new MyChannelsCompletedException();
            }

            m_queue.Enqueue(value);

            m_read_slim.Release();
        }

        public Task WriteAsync(T value)
        {
            return WriteAsync_(value);
        }

        async Task<T> ReadAsync_(bool isReportCompletedImmediately)
        {
            try
            {

                await m_read_slim.WaitAsync(m_source.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (isReportCompletedImmediately)
                {
                    throw new MyChannelsCompletedException();
                }
              
            }


            if (m_queue.TryDequeue(out T value))
            {
                m_write_slim.Release();

                return value;
            }
            else
            {
                throw new MyChannelsCompletedException();
            }


        }

        public Task<T> ReadAsync()
        {
            return ReadAsync_(false);
        }

        public Task<T> ReadReportCompletedImmediatelyAsync()
        {
            return ReadAsync_(true);
        }

        async Task<T[]> ReadRemainderAsync_()
        {
            var list = new List<T>();

            try
            {

                while (true)
                {
                    var item = await ReadAsync().ConfigureAwait(false);

                    list.Add(item);
                }
            }
            catch (MyChannelsCompletedException)
            {

            }

            return list.ToArray();

        }

        public Task<T[]> ReadRemainderAsync()
        {
            if (IsComplete)
            {
                return ReadRemainderAsync_();
            }
            else
            {
                throw new InvalidOperationException("管道未完成");
            }
        }

        int m_flag = 0;
       
        public void CompleteAdding()
        {
            if (Interlocked.Exchange(ref m_flag, 1) == 0)
            {

                m_source.Cancel();
            }

        }
    }
}