using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace LeiKaiFeng.Http
{
    [Serializable]
    public sealed class MyChannelsCompletedException : Exception
    {

    }

    public sealed class MyChannels<T> where T : class
    {
        

        readonly CancellationTokenSource m_source = new CancellationTokenSource();

        readonly ConcurrentQueue<T> m_queue = new ConcurrentQueue<T>();

        readonly SemaphoreSlim m_write_slim;

        readonly SemaphoreSlim m_read_slim;

        public MyChannels(int maxCount)
        {
            m_write_slim = new SemaphoreSlim(maxCount, maxCount);

            m_read_slim = new SemaphoreSlim(0, maxCount);
        }


        public async Task WriteAsync(T value)
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

        public async Task<T> ReadAsync()
        {
            bool canceled;

            try
            {

                await m_read_slim.WaitAsync(m_source.Token).ConfigureAwait(false);

                canceled = false;

            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }


            if (m_queue.TryDequeue(out T value))
            {
                m_write_slim.Release();

                return value;
            }
            else
            {
                if (canceled)
                {
                    throw new MyChannelsCompletedException();
                }
                else
                {
                    throw new NotSupportedException("内部实现出错");
                }
                
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