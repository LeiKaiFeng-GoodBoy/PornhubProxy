using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net;
using LeiKaiFeng.Http;

namespace LeiKaiFeng.Pornhub
{
    public sealed class PacServer
    {
        const string PAC_CONTENT_TYPE = "application/x-ns-proxy-autoconfig";

        static string CreateIf(string p, string[] hosts)
        {

            bool b1 = Regex.IsMatch(p, @"^[a-zA-Z_][a-zA-Z0-9_]*$");

            if (b1 == false)
            {
                throw new ArgumentException("p form exception");
            }

            bool b2 = hosts.All((s) => Regex.IsMatch(s, @"^[a-zA-Z0-9\.-]+$"));


            if (b2 == false)
            {
                throw new ArgumentException("host form exception");
            }

            return string.Join(" || ", hosts.Select(s => $"{p} === '{s}'"));
        }

        static string CreateIfBlack(string s, IPEndPoint endPoint)
        {
            return $"if({s}) return 'PROXY {endPoint.Address}:{endPoint.Port}';";
        }

        static string CreateAll(string p, (string[] hosts, IPEndPoint endPoint)[] values)
        {
            StringBuilder sb = new StringBuilder();

            foreach (var (hosts, endPoint) in values)
            {
                string s = CreateIf(p, hosts);



                sb.Append(CreateIfBlack(s, endPoint));
            }


            return sb.ToString();
        }

        static string CreatePac((string[] hosts, IPEndPoint endPoint)[] values)
        {
            const string P = "host";

            string s = $"function FindProxyForURL(url, {P})" + "{" + CreateAll(P, values) + "return 'DIRECT';}";

            return s;
        }


        static async Task RequestAsync(Socket socket, string s)
        {

            try
            {
                MHttpStream stream = new MHttpStream(socket, new NetworkStream(socket, true));


                await MHttpRequest.ReadAsync(stream, 1024 * 1024).ConfigureAwait(false);

                MHttpResponse response = MHttpResponse.Create(200);


                response.Headers.Set("Content-Type", PAC_CONTENT_TYPE);

                response.SetContent(s);


                await response.SendAsync(stream).ConfigureAwait(false);
            }
            catch (SocketException)
            {

            }
            
        }

        static async Task While(Socket socket, string s)
        {
            while (true)
            {

                Socket client = await socket.AcceptAsync().ConfigureAwait(false);

                Task t = Task.Run(() => RequestAsync(client, s));
            }
        }

        public static (string[] hosts, IPEndPoint endPoint) Create(IPEndPoint endPoint, params string[] hosts)
        {
            return (hosts, endPoint);
        }

        public Task Task { get; private set; }

        public static Uri CreatePacUri(IPEndPoint endPoint)
        {
            return new Uri($"http://{endPoint.Address}:{endPoint.Port}/proxy.pac");
        }

        public static PacServer Start(IPEndPoint server, params (string[] hosts, IPEndPoint endPoint)[] values)
        {
           
            string s = CreatePac(values);

            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            socket.Bind(server);

            socket.Listen(6);



            return new PacServer
            {
               
                Task = Task.Run(() => While(socket, s))
            };
        

        }

    }
}
