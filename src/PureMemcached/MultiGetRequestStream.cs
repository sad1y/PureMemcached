using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached;

internal class MultiGetRequestStream : Stream
{
    private readonly IEnumerator<KeyRequest> _requests;
    private readonly byte[] _tempBuffer;
    private Memory<byte> _temp;
    private bool _disposed;
    private bool _finished;

    private static readonly IOException CannotCopyData = new("cannot copy key data into buffer");

    private static readonly byte[] NoOpOperation;

    static MultiGetRequestStream()
    {
        NoOpOperation = new byte[Protocol.HeaderSize];
        var noOp = new Request { OpCode = OpCode.NoOp };
        Protocol.WriteHeader(ref noOp, NoOpOperation, out _);
    }

    public MultiGetRequestStream(IEnumerable<KeyRequest> requests)
    {
        _requests = requests.GetEnumerator();
        _tempBuffer = ArrayPool<byte>.Shared.Rent(Protocol.HeaderSize + Protocol.MaxKeyLength);
        _temp = Memory<byte>.Empty;
    }

    public override void Flush()
    {
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new())
    {
        var originalSize = buffer.Length;
        
        // unfinished buffer to write
        if (_temp.Length > 0)
        {
            CopyTempInto(ref buffer);
            return new ValueTask<int>(originalSize - buffer.Length);
        }

        if (_finished)
            return new ValueTask<int>(0);

        // or handle next record
        while (_requests.MoveNext())
        {
            if (_requests.Current == null)
                continue;

            var request = new Request { OpCode = OpCode.GetQ, Opaque = _requests.Current.Opaque, Key = _requests.Current.Key.Span };

            var sizeRequired = Protocol.HeaderSize + request.Key.Length;

            // we had taken the next record but if we have not enough space for it
            // we have to buffer it in other case we'll lose it
            var dest = buffer.Length < sizeRequired ? _tempBuffer.AsMemory(0, sizeRequired) : buffer;

            Protocol.WriteHeader(ref request, dest.Span, out var written);
            dest = dest[written..];
            if (!request.Key.TryCopyTo(dest.Span))
                throw CannotCopyData;

            dest = dest[request.Key.Length..];
            
            if (buffer.Length < sizeRequired)
            {
                _temp = _tempBuffer.AsMemory(0, sizeRequired);
                CopyTempInto(ref buffer);
                return new ValueTask<int>(originalSize - buffer.Length);
            }

            buffer = dest;
        }

        _finished = true;

        // if we have done with records and nothing left in temp buffer, it's time to push NoOp 
        if (_temp.Length == 0)
        {
            if (buffer.Length < NoOpOperation.Length)
            {
                _temp = NoOpOperation.AsMemory(0, NoOpOperation.Length);
                CopyTempInto(ref buffer);
            }
            else
            {
                NoOpOperation.CopyTo(buffer);
                buffer = buffer[NoOpOperation.Length..];
            }
        }

        return new ValueTask<int>(originalSize - buffer.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CopyTempInto(ref Memory<byte> buffer)
    {
        var bytesToWrite = Math.Min(buffer.Length, _temp.Length);
        var slice = _temp[..bytesToWrite];
        if (!slice.TryCopyTo(buffer))
            throw CannotCopyData;

        _temp = _temp[bytesToWrite..];
        buffer = buffer[bytesToWrite..];
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("synchronized version is not supported, consider to use Async version");

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            ArrayPool<byte>.Shared.Return(_tempBuffer);
        }
        return base.DisposeAsync();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
}