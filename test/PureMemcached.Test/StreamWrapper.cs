using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached.Test;

public class StreamWrapper : Stream
{
    private readonly byte[] _buffer;
    private int _position;
    private int _length;

    public StreamWrapper(byte[] buffer)
    {
        _buffer = buffer;
    }

    public override void Flush()
    {
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new())
    {
        var len = Math.Min(_length - _position, buffer.Length);
        var span = _buffer.AsSpan(_position, len);
        span.CopyTo(buffer.Span);
        _position += len;
        return new ValueTask<int>(len);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        _length = (int)value;
    }

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }
}