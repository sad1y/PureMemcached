using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Unicode;
using System.Threading.Tasks;
using Enyim.Caching;
using Enyim.Caching.Configuration;


namespace PureMemcached.Example
{
    public static class Program
    {
        public static async Task Main()
        {
             var timer = Stopwatch.StartNew();
        //     await PureMemcachedClient("hello");
        //     Console.WriteLine("done in: {0}", timer.Elapsed);

            timer.Restart();
            await EnyimClient("hello");
            Console.WriteLine("done in: {0}", timer.Elapsed);
        }

        private static async Task EnyimClient(string keyText)
        {
            IMemcachedClient client = new Enyim.Caching.MemcachedClient(
                new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory(),
                new MemcachedClientConfiguration(new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory(),
                    new MemcachedClientOptions
                    {
                        Servers = { new Server { Address = "localhost", Port = 11211 } }
                    } )
                );

            for (var i = 0; i < 100_000; i++)
            {
                var response = await client.GetAsync(keyText);

                Console.WriteLine(response.Value.ToString());    
            }
        }

        private static async Task PureMemcachedClient(string keyText)
        {
            var key = new byte[5];
            Encoding.UTF8.GetBytes("hello", key);

            await using var client = new MemcachedClient("127.0.0.1");
            for (var i = 0; i < 100_000; i++)
            {
                using var response = await client.Get(key).ConfigureAwait(false);
                if (response.HasError())
                {
                    var error = response.ReadErrorAsString();
                    Console.WriteLine(error);
                }
                else
                {
                    static void PrintResult(Response response)
                    {
                        Span<byte> buffer = stackalloc byte[(int)response.ExtraLength];
                        response.ReadExtra(buffer);

                        var value = string.Create((int)response.BodyLength, response, (span, state) =>
                        {
                            Span<byte> value = stackalloc byte[(int)response.BodyLength];
                            var read = response.ReadBody(value);

                            Utf8.ToUtf16(value[..read], span, out _, out _);
                        });

                        Console.WriteLine($"size: {value.Length}, value: {value}");
                    }

                    PrintResult(response);

                    // <PackageReference Include="EnyimMemcachedCore" Version="2.5.3" />
                }
            }
        }
    }
}