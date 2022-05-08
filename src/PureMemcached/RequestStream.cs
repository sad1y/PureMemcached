using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached;

/// <summary>
/// create stream from request
/// </summary>
internal class RequestStream : Stream
{
    private readonly Stream _payload;
    private readonly byte[] _header;
    private readonly int _headerLength;
    private int _position;

    internal RequestStream(ref Request request)
    {
        _header = ArrayPool<byte>.Shared.Rent(request.Key.Length + request.Extra.Length + Protocol.HeaderSize);
        _payload = request.Payload ?? Null;

        Protocol.WriteHeader(ref request, _header, out _headerLength);
        request.Extra.CopyTo(_header.AsSpan()[_headerLength..]);
        _headerLength += request.Extra.Length;
        request.Key.CopyTo(_header.AsSpan()[_headerLength..]);
        _headerLength += request.Key.Length;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = new())
    {
        // copy header
        if (_position < _headerLength)
        {
            var len = Math.Min(buffer.Length, _headerLength - _position);
            _header.AsSpan(_position, len).CopyTo(buffer.Span);
            _position += len;
            return new ValueTask<int>(len);
        }

        return ReadPayloadAsync(buffer, token);
    }
    
    private async ValueTask<int> ReadPayloadAsync(Memory<byte> buffer, CancellationToken token)
    {
        var len = await _payload.ReadAsync(buffer, token).ConfigureAwait(false);
        _position += len;
        return len;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        ArrayPool<byte>.Shared.Return(_header);
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _headerLength + _payload.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }
}