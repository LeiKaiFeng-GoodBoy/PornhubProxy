using System;
using System.Collections.Generic;

namespace LeiKaiFeng.Http
{
    public sealed class MHttpHeaders
    {


        public Dictionary<string, string> Headers { get; private set; }

        public MHttpHeaders()
        {
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        

        public string Get(string key)
        {
            return Headers[key];
        }

        public void Set(string key, string value)
        {
            Headers[key] = value;
        }

        public bool IsClose()
        {
            if (Headers.TryGetValue(MHttpHeadersKey.Connection, out var s) &&
                s.IndexOf("close", StringComparison.OrdinalIgnoreCase) != -1)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool IsChunked()
        {
            if (Headers.TryGetValue(MHttpHeadersKey.TransferEncoding, out var s) &&
                s.Equals("chunked", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool IsText()
        {
            if (Headers.TryGetValue(MHttpHeadersKey.ContentType, out var s) &&
                s.IndexOf("text/", StringComparison.OrdinalIgnoreCase) != -1)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public void SetMyStandardRequestHeaders()
        {
            //identity
            this.Set("Accept-Encoding", "deflate, gzip");
            this.Set("Accept-Charset", "utf-8");
            SetMyStandardHeaders();
        }

        public void SetMyStandardHeaders()
        {
            this.Set("Connection", "keep-alive");
        }

        public void SetContentLength(int length)
        {
            if(length < 0)
            {
                throw new MHttpStreamException("设置内容长度超出范围");
            }

            this.Headers[MHttpHeadersKey.ContentLength] = length.ToString();
        }

        public int GetContentLength()
        {
            int n = int.Parse(Headers[MHttpHeadersKey.ContentLength]);

            if (n < 0)
            {
                throw new MHttpStreamException("获取内容长度超出范围");
            }
            else
            {
                return n;
            }
        }

        public bool TryGetContentLength(out int length)
        {
            if (Headers.ContainsKey(MHttpHeadersKey.ContentLength))
            {
                length = GetContentLength();

                return true;
            }
            else
            {
                length = 0;

                return false;
            }
        }

        static string[] GetHopByHopHeaders()
        {
            return new string[] {
                "Keep-Alive", "Transfer-Encoding", "TE",
                "Connection", "Trailer", "Upgrade",
                "Proxy-Authorization","Proxy-Authenticate"};
        }

        public void RemoveContentEncoding()
        {
            Headers.Remove(MHttpHeadersKey.ContentEncoding);
        }

        public void RemoveHopByHopHeaders()
        {
            if (Headers.TryGetValue(MHttpHeadersKey.Connection, out string s))
            {
                foreach (var item in s.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Headers.Remove(item.Trim());
                }
            }

            foreach (var item in GetHopByHopHeaders())
            {
                Headers.Remove(item);
            }
        }
    }
}