using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Channels;

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

        readonly ConcurrentDictionary<HostKey, Channel<MHttpStreamPack>> m_pool = new ConcurrentDictionary<HostKey, Channel<MHttpStreamPack>>();

        readonly Func<HostKey, Channel<MHttpStreamPack>> m_create;

        public StreamPool(int maxCount)
        {
            m_create = (k) => Channel.CreateBounded<MHttpStreamPack>(maxCount);
        }

     
        Channel<MHttpStreamPack> Find(Uri uri)
        {
            return m_pool.GetOrAdd(new HostKey(uri), m_create);
        }

        public bool Get(Uri uri, out MHttpStreamPack pack)
        {
            var channel = Find(uri);

            return channel.Reader.TryRead(out pack);
        }

        public bool Set(Uri uri, MHttpStreamPack pack)
        {
            var channel = Find(uri);

            return channel.Writer.TryWrite(pack);
        }
    }
}