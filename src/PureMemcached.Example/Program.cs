using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using Enyim.Caching.Configuration;


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

            await Parallel.ForEachAsync(Enumerable.Range(0, 1), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (a, b) =>
            {
                await using var response = await client.Get(key.AsSpan()[..written], token: b).ConfigureAwait(false);
                if (response.HasError())
                {
                    var buffer = new ArrayBufferWriter<byte>(256);
                    await response.CopyErrorAsync(buffer, b);
                    Console.WriteLine("error {}", Encoding.UTF8.GetString(buffer.WrittenSpan));
                }
                else
                {
                    static async Task PrintResult(Response response, CancellationToken token)
                    {
                        var buffer = ArrayPool<byte>.Shared.Rent((int)response.BodyLength);
                        try
                        {
                            await response.SkipExtraAsync(token);

                            await using var body = response.GetStream();
                            var offset = 0;
                            do
                            {
                                var read = await body.ReadAsync(buffer.AsMemory(offset), token);
                                offset += read;
                            } while (body.Length != body.Position);

                            Console.WriteLine("value: {0}", Encoding.UTF8.GetString(buffer.AsSpan(0, offset)));
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }

                    await PrintResult(response, b);
                }
            });
        }
    }
}