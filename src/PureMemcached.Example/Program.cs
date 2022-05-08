using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Unicode;
using System.Threading.Tasks;
using Enyim.Caching.Configuration;
using PureMemcached.Example.Extensions;

namespace PureMemcached.Example
{
    public static class Program
    {
        public static async Task Main()
        {
            var timer = Stopwatch.StartNew();
            await PureMemcachedClient("hello");
            var t1 = timer.Elapsed;
            var mem1 = GC.GetTotalAllocatedBytes(true);
            
            timer.Restart();
            await EnyimClient("hello");
            var t2 = timer.Elapsed;
            var mem2 = GC.GetTotalAllocatedBytes(true);
            
            Console.WriteLine("done in: t1 {0}, t2 {1}", t1, t2);
            Console.WriteLine("mem1: {0}, mem2: {1}", mem1, mem2);
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
            await Parallel.ForEachAsync(Enumerable.Range(0, 100000), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (a, b) =>
            {
                var response = await client.GetAsync(keyText);
                Console.WriteLine(response.Value.ToString());
            });
        }

        private static async Task PureMemcachedClient(string keyText)
        {
            await using var client = new MemcachedClient("localhost");

            var key = new byte[32];
            Utf8.FromUtf16(keyText, key, out _, out var written);

            await Parallel.ForEachAsync(Enumerable.Range(0, 100000), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (a, cancellationToken) =>
            {
                await using var response = await client.Get(key.AsSpan()[..written], token: cancellationToken).ConfigureAwait(false);
                await using var body = response.GetBody();

                var responseText = await body.ReadAsUtf8StringAsync(cancellationToken);
                Console.WriteLine(responseText);
            });
        }
    }
}