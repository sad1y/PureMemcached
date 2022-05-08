using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PureMemcached.Extensions;

namespace PureMemcached;

/// <summary>
/// this class used as abstraction for multi-response stream.
/// it allows you to iterate over responses using fire-and-forget approach.
/// it is important to consume all data from each request otherwise it will be lost
/// </summary>
public class MultiResponse : IAsyncEnumerable<Response>, IAsyncDisposable
{
    private readonly Stream _stream;

    internal MultiResponse(Stream stream)
    {
        _stream = stream;
    }
    
    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    public async IAsyncEnumerator<Response> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if(_stream == null || _stream == Stream.Null)
            yield break;
        
        var multiResponseStream = new MultiResponseStream(_stream);
        
        while (true)
        {
            var response = await Response.FromStream(multiResponseStream, cancellationToken).ConfigureAwait(false);
            if (response.OpCode == OpCode.NoOp)
                break;

            yield return response;

            await multiResponseStream.ReadOutAsync(cancellationToken).ConfigureAwait(false);
        }

        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Proxy class that used to mitigate any destructive action like <c>Dispose</c> on actual stream   
    /// </summary>
    private class MultiResponseStream : Stream
    {
        private static readonly byte[] DevNull = new byte[4096];
        
        private readonly Stream _underlyingStream;
        private long _offset;

        public MultiResponseStream(Stream underlyingStream)
        {
            _underlyingStream = underlyingStream;
        }

        internal async ValueTask ReadOutAsync(CancellationToken token)
        {
            // read out everything that was not consumed to be prepared for next response
            while (_underlyingStream.Length != _underlyingStream.Position) 
                await _underlyingStream.ReadExactAsync(DevNull, token).ConfigureAwait(false);

            _offset = _underlyingStream.Length;
        }
        
        public override void Flush() => _underlyingStream.Flush();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new()) => 
            _underlyingStream.ReadAsync(buffer, cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => _underlyingStream.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _underlyingStream.Seek(offset, origin);

        public override void SetLength(long value) => _underlyingStream.SetLength(_offset + value);

        public override void Write(byte[] buffer, int offset, int count) => _underlyingStream.Write(buffer, offset, count);
        
        public override bool CanRead => _underlyingStream.CanRead;
        public override bool CanSeek => _underlyingStream.CanSeek;
        public override bool CanWrite => _underlyingStream.CanWrite;
        public override long Length => _underlyingStream.Length - _offset;

        public override long Position
        {
            get => _underlyingStream.Position - _offset;
            set => throw new NotSupportedException();
        }
    }
}