using System;
using System.Threading.Tasks;
using LeiKaiFeng.Http;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace Test
{
    class Program
    {
        static async Task TestString()
        {
            MHttpClient client = new MHttpClient();

            foreach (var item in Enumerable.Range(1, 100))
            {

                string s = await client.GetStringAsync(new Uri("https://konachan.net/post?page=" + item + "&tags="), CancellationToken.None);

                Console.WriteLine(s.Length);
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
