using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached.Extensions;

public static class StreamExtensions
{
    public static async Task ReadExactAsync(this Stream stream, Memory<byte> b, CancellationToken token)
    {
        var read = 0;
        do
        {
            var len = await stream.ReadAsync(b[read..], token);
            read += len;
        } while (read != b.Length);
    }
    
    public static async Task CopyTo(this Stream stream, IBufferWriter<byte> writer, long limit, CancellationToken token)
    {
        const long maxChunkSize = 1024;

        if (limit == 0) return;
        while (stream.Length - stream.Position > 0 && limit != 0)
        {
            var span = writer.GetMemory((int)Math.Min(limit, maxChunkSize));
            var read = await stream.ReadAsync(span[..(int)limit], token);
            writer.Advance(read);
            limit -= read;
        }
    }
}