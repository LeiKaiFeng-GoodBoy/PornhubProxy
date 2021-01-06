using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net;

namespace Pornhub
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

            bool b2 = hosts.All((s) => Regex.IsMatch(s, @"^[a-zA-Z0-9\.]+$"));


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

            foreach (var item in values)
            {
                string s = CreateIf(p, item.hosts);



                sb.Append(CreateIfBlack(s, item.endPoint));
            }


            return sb.ToString();
        }

        static string CreatePac((string[] hosts, IPEndPoint endPoint)[] values)
        {
            const string P = "host";

            string s = $"function FindProxyForURL(url, {P})" + "{" + CreateAll(P, values) + "return 'DIRECT';}";

            return s;
        }

        static Task ReadRequestAsync(Socket socket)
        {
            byte[] buffer = new byte[1024];

            return socket.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None);
        }


        static async Task RequestAsync(Socket socket, byte[] buffer)
        {

            try
            {
                await ReadRequestAsync(socket).ConfigureAwait(false);

                await socket.SendAsync(new ArraySegment<byte>(buffer), SocketFlags.None).ConfigureAwait(false);

                socket.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException)
            {

            }
            finally
            {
                socket.Close();
            }
        }

        static async Task While(Socket socket, byte[] buffer)
        {
            while (true)
            {

                Socket client = await socket.AcceptAsync().ConfigureAwait(false);

                Task t = Task.Run(() => RequestAsync(client, buffer));
            }
        }

        public static (string[] hosts, IPEndPoint endPoint) Create(IPEndPoint endPoint, params string[] hosts)
        {
            return (hosts, endPoint);
        }

        public static Uri Start(IPEndPoint server, params (string[] hosts, IPEndPoint endPoint)[] values)
        {
            Uri uri = new Uri($"http://{server.Address}:{server.Port}/proxy.pac");

            string s = CreatePac(values);

            byte[] buffer = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.UTF8.GetBytes(s)}\r\nContent-Type: {PAC_CONTENT_TYPE}\r\n\r\n{s}");

            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            socket.Bind(server);

            socket.Listen(6);

            Task.Run(() => While(socket, buffer));

            return uri;
        }

    }
}
