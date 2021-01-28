using System;
using System.Threading.Tasks;
using LeiKaiFeng.Http;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;

namespace Test
{
    class Program
    {
        static async Task TestString()
        {
            MHttpClient client = new MHttpClient();

            foreach (var item in Enumerable.Range(1, 27))
            {

                string s = await client.GetStringAsync(new Uri("https://yandere.pp.ua/post/popular_by_day?day="+ item+"&month=1&year=2021"), CancellationToken.None);

                Regex regex = new Regex(@"<a class=""directlink (?:largeimg|smallimg)"" href=""([^""]+)""");




                Console.WriteLine(regex.Matches(s).Count);
            }

        }

        static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;

            try
            {
                await TestString();
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
            }

            Console.ReadLine();
            
        }

        private static void CurrentDomain_FirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            Console.WriteLine(e.Exception);
        }
    }
}
