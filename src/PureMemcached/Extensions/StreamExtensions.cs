using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached.Extensions;

public static class StreamExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static async Task ReadExactAsync(this Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        var totalRead = 0;
        do
        {
            var read = await stream.ReadAsync(buffer[totalRead..], token).ConfigureAwait(false);

            if (read == 0)
                throw new IOException("not enough data to copy");
            
            totalRead += read;
        } while (totalRead != buffer.Length);
    }
}