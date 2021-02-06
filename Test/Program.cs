using System;
using System.Threading.Tasks;
using LeiKaiFeng.Http;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;
using System.IO;

namespace Test
{
    class Program
    {
        static async Task TestString(MHttpClient client)
        {
           
            foreach (var item in Enumerable.Range(1, 27))
            {

                try
                {
                    string s = await client.GetStringAsync(new Uri("https://yandere.pp.ua/post/popular_by_day?day=" + item + "&month=1&year=2021"), CancellationToken.None);

                    Regex regex = new Regex(@"<a class=""directlink (?:largeimg|smallimg)"" href=""([^""]+)""");




                    Console.WriteLine(regex.Matches(s).Count);
                }
                catch (MHttpClientException e)
                when(e.InnerException is OperationCanceledException)
                {

                }

                
            }

        }

        static async Task Main(string[] args)
        {
            //AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;

            MHttpClient client = new MHttpClient();

            client.ConnectTimeOut = new TimeSpan(0, 0, 2);

            var list = new List<Task>();

            foreach (var item in Enumerable.Range(0, 6)) 
            {
                list.Add(TestString(client));
            }

            try
            {
                await Task.WhenAll(list.ToArray());
            }
            catch(Exception e)
            {
                string s = Environment.NewLine;

                File.AppendAllText("log.txt", $"{e}{s}{s}");
            }

            Console.ReadLine();
            
        }

        private static void CurrentDomain_FirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            Console.WriteLine(e.Exception);
        }
    }
}
