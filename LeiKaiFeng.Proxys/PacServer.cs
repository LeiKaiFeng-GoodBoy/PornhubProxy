using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net;
using LeiKaiFeng.Http;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace LeiKaiFeng.Pornhub
{
    public abstract class ProxyMode
    {
        sealed class DIRECTMode : ProxyMode
        {
            public override string GetText()
            {
                return "DIRECT";
            }
        }

        sealed class HTTPMode : ProxyMode
        {
            public HTTPMode(IPEndPoint iPEndPoint)
            {
                IPEndPoint = iPEndPoint;
            }

            IPEndPoint IPEndPoint { get; }



            public override string GetText()
            {
                return $"'PROXY {IPEndPoint.Address}:{IPEndPoint.Port}'";
            }
        }

        public static ProxyMode CreateDIRECT()
        {
            return new DIRECTMode();
        }


        public static ProxyMode CreateHTTP(IPEndPoint ipendPoint)
        {
            return new HTTPMode(ipendPoint);
        }

        public abstract string GetText();
    }

    public static class PacHelper
    {
        const string HOST_PAR_NAME = "host";


        static void ThrowMethodException(MethodCallExpression expression)
        {
            if (expression.Method.Name == nameof(PacMethod.Equals) ||
                expression.Method.Name == nameof(PacMethod.ReferenceEquals) ||
                expression.Method.ReflectedType != typeof(PacMethod))
            {
                throw new ArgumentException("表达式中调用了不支持的方法");
            }
        }

        static void ThrowUriException(string host)
        {
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                throw new UriFormatException(host);
            }
        }

        static string CreateEqualOrNotEqual(Expression left, Expression right, ParameterExpression parameter, bool isEqual)
        {
            string operator_text = isEqual ? "===" : "!==";


            if (left is ConstantExpression constant && constant.Value is string value)
            {
                if (object.ReferenceEquals(right, parameter))
                {
                    ThrowUriException(value);

                    return $"'{value}'{operator_text}{HOST_PAR_NAME}";
                }
                else
                {
                    throw new ArgumentException("参数表达式是其他对象");
                }
            }
            else
            {
                if (object.ReferenceEquals(left, parameter))
                {
                    if (right is ConstantExpression constant2 && constant2.Value is string value2)
                    {
                        ThrowUriException(value2);

                        return $"{HOST_PAR_NAME}{operator_text}'{value2}'";
                    }
                    else
                    {
                        throw new ArgumentException("表达式不是Const表达式");
                    }
                }
                else
                {
                    throw new ArgumentException("参数表达式是其他对象,或者另一个表达式不是Const表达式");
                }
            }
        }

        static string CreateMethod(ParameterExpression par, IEnumerable<Expression> expressions)
        {

            var list = expressions.Select((exp) =>
            {
                if (exp is ConstantExpression constant && constant.Value is string s)
                {
                    ThrowUriException(s);

                    return $"'{s}'";
                }
                else if (exp is ParameterExpression)
                {
                    if (object.ReferenceEquals(par, exp))
                    {
                        return HOST_PAR_NAME;
                    }
                    else
                    {
                        throw new ArgumentException("参数表达式是其他对象");
                    }
                }
                else
                {
                    throw new ArgumentException("参数表达式是其他对象,或者其他表达式不是Const表达式");
                }
            }).ToArray();

            string ss = string.Join(",", list);

            return $"({ss})";
        }

        static string CreateIf(Expression<Func<string, bool>> expression)
        {
            var par = expression.Parameters.First();

            var body = expression.Body;

            if (body is MethodCallExpression methodCallExpression)
            {
                ThrowMethodException(methodCallExpression);

                string methodName = methodCallExpression.Method.Name;

                string methodParList = CreateMethod(par, methodCallExpression.Arguments);

                return $"{methodName}{methodParList}";
            }
            else
            if (body is BinaryExpression binaryExpression &&
                    (binaryExpression.NodeType == ExpressionType.Equal ||
                    binaryExpression.NodeType == ExpressionType.NotEqual))
            {

                return CreateEqualOrNotEqual(binaryExpression.Left,
                    binaryExpression.Right,
                    par,
                    binaryExpression.NodeType == ExpressionType.Equal);
            }
            else
            {
                throw new ArgumentException("表达式不支持");
            }
        }


        static string CreateIfBlock(Expression<Func<string, bool>> expression, ProxyMode proxyMode)
        {
            var ifText = CreateIf(expression);

            var s = proxyMode.GetText();

            return $"if({ifText})return  {s};";
        }

        public static KeyValuePair<Expression<Func<string, bool>>, ProxyMode> Create(Expression<Func<string, bool>> expression, ProxyMode proxyMode)
        {
            return new KeyValuePair<Expression<Func<string, bool>>, ProxyMode>(expression, proxyMode);
        }

        public static string CreateFunc(params KeyValuePair<Expression<Func<string, bool>>, ProxyMode>[] pair)
        {

            var sb = new StringBuilder();
            sb.Append('{');
            Array.ForEach(pair, (item) => sb.Append(CreateIfBlock(item.Key, item.Value)));
            sb.Append('}');


            return $"function FindProxyForURL(url, {HOST_PAR_NAME}){sb}";
        }
    }

    public static class PacMethod
    {
        public static bool dnsDomainIs(string host, string domain)
        {
            throw new NotImplementedException();
        }
    }

    //PAC文件默认情况下返回空
    public sealed class PacServer
    {
        

        public sealed class Builder
        {
            readonly IPEndPoint m_listenIPEndPoint;

            readonly List<KeyValuePair<Expression<Func<string, bool>>, ProxyMode>> m_list;

            private Builder(IPEndPoint ipEndPoint)
            {
                m_listenIPEndPoint = ipEndPoint;

                m_list = new List<KeyValuePair<Expression<Func<string, bool>>, ProxyMode>>();
            }

            public static Builder Create(IPEndPoint ipEndPoint)
            {
                return new Builder(ipEndPoint);
            }

            public Builder Add(Expression<Func<string, bool>> expression, ProxyMode proxyMode)
            {
                m_list.Add(new KeyValuePair<Expression<Func<string, bool>>, ProxyMode>(expression, proxyMode));

                return this;
            }



            public PacServer StartPACServer()
            {
                return PacServer.Start(m_listenIPEndPoint, m_list.ToArray());
            }
        }

        const string PAC_CONTENT_TYPE = "application/x-ns-proxy-autoconfig";


        static async Task RequestAsync(Socket socket, byte[] buffer)
        {
            MHttpStream stream = new MHttpStream(socket, new NetworkStream(socket, true), 1024);


            await MHttpRequest.ReadAsync(stream, 1024 * 1024).ConfigureAwait(false);

            MHttpResponse response = MHttpResponse.Create(200);


            response.Headers.Set("Content-Type", PAC_CONTENT_TYPE);

            response.SetContent(buffer);


            await response.SendAsync(stream).ConfigureAwait(false);
        }

        public static Uri CreatePacUri(IPEndPoint endPoint)
        {
            return new Uri($"http://{endPoint.Address}:{endPoint.Port}/proxy.pac");
        }


       
        static PacServer Start(IPEndPoint server, KeyValuePair<Expression<Func<string, bool>>, ProxyMode>[] pair)
        {

            var buffer = Encoding.UTF8.GetBytes(PacHelper.CreateFunc(pair));



            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            socket.Bind(server);

            socket.Listen(6);

            Task task = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {

                        Socket connect = await socket.AcceptAsync().ConfigureAwait(false);

                        Task t = Task.Run(() => RequestAsync(connect, buffer));
                    }
                }
                catch (ObjectDisposedException)
                {

                }
            });



            return new PacServer
            {

                Task = task,

                ListenSocket = socket
            };
        

        }


        public Task Task { get; private set; }

        Socket ListenSocket { get; set; }

        public void Cancel()
        {
            ListenSocket.Close();
        }

    }
}