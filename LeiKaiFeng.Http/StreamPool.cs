using System;
using System.Collections.Concurrent;

namespace LeiKaiFeng.Http
{

    sealed class StreamPool
    {

        sealed class HostKey : IEquatable<HostKey>
        {
            string m_protocol;

            string m_host;

            int m_port;

            public HostKey(Uri uri)
            {
                m_protocol = uri.Scheme;

                m_host = uri.Host;

                m_port = uri.Port;
            }

            public bool Equals(HostKey other)
            {
                if (other is null)
                {
                    return false;
                }


                if (object.ReferenceEquals(this, other))
                {
                    return true;
                }


                if (m_protocol.Equals(other.m_protocol, StringComparison.OrdinalIgnoreCase) &&
                    m_host.Equals(other.m_host, StringComparison.OrdinalIgnoreCase) &&
                    m_port == other.m_port)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public override bool Equals(object obj)
            {
                return this.Equals(obj as HostKey);
            }

            public override int GetHashCode()
            {
                var v = StringComparer.OrdinalIgnoreCase;

                int a = v.GetHashCode(m_protocol);

                int b = v.GetHashCode(m_host);

                int c = m_port.GetHashCode();

                return a | b | c;
            }
        }



        readonly ConcurrentDictionary<HostKey, ConcurrentQueue<MHttpStream>> m_pool = new ConcurrentDictionary<HostKey, ConcurrentQueue<MHttpStream>>();

        readonly int m_maxCount;

        public StreamPool(int maxCount)
        {
            m_maxCount = maxCount;
        }

        ConcurrentQueue<MHttpStream> Find(Uri uri)
        {
            return m_pool.GetOrAdd(new HostKey(uri), (k) => new ConcurrentQueue<MHttpStream>());
        }

        public MHttpStream Get(Uri uri)
        {
            var queue = Find(uri);

            if (queue.TryDequeue(out var v))
            {
                return v;
            }
            else
            {
                return null;
            }
        }

        public void Set(Uri uri, MHttpStream stream)
        {
           

            var queue = Find(uri);


            while (queue.Count > m_maxCount && queue.TryDequeue(out _))
            {

            }

            queue.Enqueue(stream);
        }
    }
}