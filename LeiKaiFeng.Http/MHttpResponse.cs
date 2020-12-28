using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace LeiKaiFeng.Http
{
    public sealed class MHttpResponse
    {


        private MHttpResponse(int status, MHttpHeaders headers)
        {
            Status = status;

            Headers = headers;

            Content = new MHttpContent();
        }

        public MHttpHeaders Headers { get; private set; }

        public int Status { get; private set; }

        public MHttpContent Content { get; private set; }

        static int ParseStatus(string firstLine)
        {
            string[] ss = firstLine.Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);

            return int.Parse(ss[1]);
        }

        static MHttpResponse Create(ArraySegment<byte> buffer)
        {

            var firstDic = MHttpParse.ParseLine(buffer);

            int status = ParseStatus(firstDic.Key);

            return new MHttpResponse(status, firstDic.Value);

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


        public static MHttpResponse Create(int status)
        {
            return new MHttpResponse(status, new MHttpHeaders());
        }

        Task ReadContentAsync(MHttpStream stream, int maxContentSize)
        {
            return Content.ReadAsync(stream, Headers, maxContentSize);
        }

        internal static Task<MHttpResponse> ReadHeadersAsync(MHttpStream stream)
        {
            return stream.ReadHeadersAsync(Create);
        }

        public static async Task<MHttpResponse> ReadAsync(MHttpStream stream, int maxContentSize)
        {
            MHttpResponse response = await ReadHeadersAsync(stream).ConfigureAwait(false);

            await response.ReadContentAsync(stream, maxContentSize).ConfigureAwait(false);

            return response;
        }


        internal Task SendHeadersAsync(MHttpStream stream)
        {
            string firstLine = $"HTTP/1.1 {Status} OK";

            byte[] buffer = MHttpCreate.EncodeHeaders(firstLine, Headers.Headers);

            return stream.WriteAsync(buffer);
        }


        public async Task SendAsync(MHttpStream stream)
        {
            await SendHeadersAsync(stream).ConfigureAwait(false);

            await Content.SendAsync(stream).ConfigureAwait(false);
        }
    }
}