using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Unicode;
using System.Threading.Tasks;
using Enyim.Caching.Configuration;
using PureMemcached.Network;


namespace PureMemcached.Example
{
    public static class Program
    {
        public static async Task Main()
        {
            var timer = Stopwatch.StartNew();
            await PureMemcachedClient("hello");
            var t1 = timer.Elapsed;

            timer.Restart();
            await EnyimClient("hello");
            var t2 = timer.Elapsed;
            
            Console.WriteLine("done in: t1 {0}, t2 {1}", t1, t2);
        }

        private static async Task EnyimClient(string keyText)
        {
            var logger = new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory();
            var cfg = new MemcachedClientConfiguration(logger,
                new MemcachedClientOptions
                {
                    Servers = { new Server { Address = "localhost", Port = 11211 } }
                });
            
            using var client = new Enyim.Caching.MemcachedClient(logger, cfg);
            await Parallel.ForEachAsync(Enumerable.Range(0, 100_000), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (a, b) =>
            {
                var response = await client.GetAsync(keyText);
                Console.WriteLine(response.Value.ToString());
            });
        }

        private static async Task PureMemcachedClient(string keyText)
        {
            var socketFactory = new SocketConnectionFactory("localhost", 11211, 1024, 1024, 100, 100, TimeSpan.FromMinutes(5));
            using var memCachedClientPool = new MemcachedClientPool(socketFactory);

            await Parallel.ForEachAsync(Enumerable.Range(0, 100_000), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (a, b) =>
            {
                var key = new byte[32];
                Utf8.FromUtf16(keyText, key, out _, out var written);
                
                await using var client = await memCachedClientPool.RentAsync();
                using var response = await client.Get(key.AsSpan()[..written], token: b).ConfigureAwait(false);
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
                            Span<byte> value = stackalloc byte[(int)state.BodyLength];
                            var read = state.ReadBody(value);

                            Utf8.ToUtf16(value[..read], span, out _, out _);
                        });

                        Console.WriteLine("value: {0}", value);
                    }

                    PrintResult(response);
                }
            });
        }
    }
}