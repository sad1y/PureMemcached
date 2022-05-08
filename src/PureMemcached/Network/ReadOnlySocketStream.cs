using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached.Network;

public class ReadOnlySocketStream : Stream
{
    private readonly Socket _socket;
    private readonly IAsyncDisposable _owner;
    private long _length;
    private long _position;
    private bool _disposed;

    internal ReadOnlySocketStream(Socket socket, IAsyncDisposable owner)
    {
        _socket = socket;
        _owner = owner;
    }

    public override void Flush()
    {
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new())
    {
        if (_position == _length || _disposed)
            return new ValueTask<int>(0);

        async ValueTask<int> ReadInternalAsync(Memory<byte> writeInto, CancellationToken token)
        {
            var read = await _socket.ReceiveAsync(writeInto, SocketFlags.None, token).ConfigureAwait(false);
            _position += read;
            return read;
        }

        // do not allow to read more that length
        var size = (int)Math.Min(buffer.Length, _length - _position);   

        return ReadInternalAsync(buffer[..size], cancellationToken);
    }

    public override ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            return _owner.DisposeAsync();
        }

        return new ValueTask();
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("you should use Async version");

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => _length = value;

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