using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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

        sealed class Deque
        {
            readonly object m_lock = new object();

            readonly LinkedList<MHttpStream> m_list;

            readonly int m_maxCount;

            public Deque(int maxCount)
            {
                m_maxCount = maxCount;

                m_list = new LinkedList<MHttpStream>();
            }


            public MHttpStream Add(MHttpStream stream)
            {
                lock (m_lock)
                {
                    m_list.AddFirst(stream);

                    if (m_list.Count >= m_maxCount)
                    {
                        var v = m_list.Last.Value;

                        m_list.RemoveLast();

                        return v;
                    }
                    else
                    {
                        return null;
                    }
                    
                }
            }

            public MHttpStream Get()
            {
                lock (m_lock)
                {
                    var node = m_list.First;

                    if (node is null)
                    {
                        return null;
                    }
                    else
                    {
                        var stream = node.Value;

                        m_list.RemoveFirst();

                        return stream;
                    }
                }
            }
        }

        readonly ConcurrentDictionary<HostKey, Deque> m_pool = new ConcurrentDictionary<HostKey, Deque>();

        readonly Func<HostKey, Deque> m_create;

        public StreamPool(int maxCount)
        {
            m_create = (k) => new Deque(maxCount);
        }

     
        Deque Find(Uri uri)
        {
            return m_pool.GetOrAdd(new HostKey(uri), m_create);
        }

        public MHttpStream Get(Uri uri)
        {
            var deque = Find(uri);

            return deque.Get();
        }

        public void Set(Uri uri, MHttpStream stream)
        {
            var deque = Find(uri);

            deque.Add(stream)?.Close();
        }
    }
}