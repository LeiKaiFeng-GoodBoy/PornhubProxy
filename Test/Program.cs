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
            await TestString();
        }
    }
}
