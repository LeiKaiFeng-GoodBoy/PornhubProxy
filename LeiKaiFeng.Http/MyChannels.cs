using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace LeiKaiFeng.Http
{
    public sealed class MyChannels<T> where T : class
    {
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
            await m_write_slim.WaitAsync().ConfigureAwait(false);

            m_queue.Enqueue(value);

            await Task.Run(m_read_slim.Release).ConfigureAwait(false);
        }

        public async Task<T> ReadAsync()
        {
            await m_read_slim.WaitAsync().ConfigureAwait(false);


            if (m_queue.TryDequeue(out T value))
            {
                await Task.Run(m_write_slim.Release).ConfigureAwait(false);

                return value;
            }
            else
            {
                throw new NotSupportedException("内部实现出错");
            }

            
        }

    }
}