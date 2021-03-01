using LeiKaiFeng.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LeiKaiFeng.Proxys
{
    public static class ConnectHelper
    {



        public static async Task<T> ReadConnectRequestAsync<T>(Stream stream, Func<Stream, string, T> func)
        {
            MHttpStream httpStream = new MHttpStream(default, stream, 1024);


            MHttpRequest request = await MHttpRequest.ReadAsync(httpStream, 1024 * 1024).ConfigureAwait(false);

            MHttpResponse response = MHttpResponse.Create(200);


            await response.SendAsync(httpStream).ConfigureAwait(false);

           
            return func(stream, request.Path.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries)[0]);
        }

    }
}
