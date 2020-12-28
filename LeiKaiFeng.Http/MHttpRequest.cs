using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace LeiKaiFeng.Http
{
    public sealed class MHttpRequest
    {
        public string Method { get; private set; }

        public string Path { get; private set; }

        public MHttpHeaders Headers { get; private set; }

        public MHttpContent Content { get; private set; }

        private MHttpRequest(string method, string path, MHttpHeaders headers)
        {
            Method = method;

            Path = path;

            Headers = headers;

            Content = new MHttpContent();
        }

        static KeyValuePair<string, string> ParseMethodUri(string firstLine)
        {
            string[] ss = firstLine.Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);

            return new KeyValuePair<string, string>(ss[0], ss[1]);
        }


        static MHttpRequest Create(ArraySegment<byte> buffer)
        {

            var firstDic = MHttpParse.ParseLine(buffer);

            var mu = ParseMethodUri(firstDic.Key);

            return new MHttpRequest(mu.Key, mu.Value, firstDic.Value);
        }

        public static MHttpRequest Create(string method, string path)
        {
            return new MHttpRequest(method, path, new MHttpHeaders());
        }

        public static MHttpRequest CreateGet(Uri uri)
        {
            MHttpRequest request = Create("GET", uri.PathAndQuery);

            var headers = request.Headers;

            headers.Set("Host", uri.Host);

            headers.SetMyStandardRequestHeaders();

            headers.Set("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");
 
            headers.Set("Accept-Language", "zh-CN,zh;q=0.9");
        
            headers.Set("User-Agent", "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.75 Safari/537.36");


            return request;
        }

        public void SetContent(string s)
        {
            SetContent(Encoding.UTF8.GetBytes(s));
        }


        public void SetContent(byte[] array)
        {
            Headers.SetContentLength(array.Length);

            Headers.RemoveContentEncoding();

            Content.SetByteArray(array);
        }


        Task ReadContentAsync(MHttpStream stream, int maxContentSize)
        {
            if (Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        internal static Task<MHttpRequest> ReadHeadersAsync(MHttpStream stream)
        {
            return stream.ReadHeadersAsync(Create);
        }

        public static async Task<MHttpRequest> ReadAsync(MHttpStream stream, int maxContentSize)
        {
            MHttpRequest request = await ReadHeadersAsync(stream).ConfigureAwait(false);

            await request.ReadContentAsync(stream, maxContentSize).ConfigureAwait(false);

            return request;
        }

        byte[] AndHeadersContent()
        {
            return CreateRequestHeadersByteArray().Concat(Content.GetByteArray()).ToArray();
        }

        byte[] CreateRequestHeadersByteArray()
        {
            string firstLine = $"{Method} {Path} HTTP/1.1";
         
            return MHttpCreate.EncodeHeaders(firstLine, Headers.Headers);

        }


        internal Task SendHeadersAsync(MHttpStream stream)
        {
            byte[] buffer = CreateRequestHeadersByteArray();
            
            return stream.WriteAsync(buffer);

        }

        public Func<MHttpStream, Task> CreateSendAsync()
        {
            byte[] buffer = AndHeadersContent();

            return (stream) => stream.WriteAsync(buffer);
        }

        public async Task SendAsync(MHttpStream stream)
        {
            await SendHeadersAsync(stream).ConfigureAwait(false);

            await Content.SendAsync(stream).ConfigureAwait(false);
        }
    }
}