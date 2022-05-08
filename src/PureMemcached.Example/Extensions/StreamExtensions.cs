using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached.Example.Extensions;

public static class StreamExtensions
{
    public static async Task<string> ReadAsUtf8StringAsync(this Stream stream, CancellationToken token)
    {
        var sb = new StringBuilder();
        var buffer = ArrayPool<byte>.Shared.Rent(256);
        try
        {
            var size = 0;
            var offset = 0; 
            do
            {
                size = await stream.ReadAsync(buffer.AsMemory(offset), token);
                WriteChars(sb, buffer.AsMemory(offset, size), out var read, out _);

                if (size != read)
                {
                    buffer.AsMemory(read).CopyTo(buffer);
                    offset = buffer.Length - read;
                }
                else
                {
                    offset = 0;
                }

            } while (size != 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        static void WriteChars(StringBuilder sb, ReadOnlyMemory<byte> buffer, out int read, out int written)
        {
            if (buffer.Length == 0)
            {
                read = 0;
                written = 0;
                return;
            }
            
            Span<char> ch = stackalloc char[buffer.Length];

            if (Utf8.ToUtf16(buffer.Span, ch, out read, out written, false, false) != OperationStatus.Done)
                throw new IOException("cannot read utf8 data from buffer");

            sb.Append(ch[..written]);
        }
        
        return sb.ToString();
    }
    
    
}