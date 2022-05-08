using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PureMemcached.Extensions;

namespace PureMemcached
{
    public class Response : IAsyncDisposable
    {
        private static readonly ArrayPool<byte> PoolBuffer = ArrayPool<byte>.Create(512, 10);

        private readonly ResponseHeader _header;
        private readonly Stream _stream;
        private readonly byte[]? _headerPayload;

        public bool InvalidCas { get; private set; }

        public Status Status => _header.Status;

        public ulong Cas => _header.Cas;

        public uint Opaque => _header.Opaque;

        public long BodyLength => _header.TotalSize - (_header.KeyLength + _header.ExtraLength);

        public OpCode OpCode => _header.OpCode;
        internal long TotalSize => _header.TotalSize;

        internal Response(ResponseHeader header, byte[]? headerPayload, Stream stream)
        {
            _header = header;
            _stream = stream;
            _headerPayload = headerPayload;
        }

        public bool HasError() => InvalidCas || Status != Status.NoError;

        public ReadOnlySpan<byte> Extra => _headerPayload.AsSpan(0, _header.ExtraLength);

        public ReadOnlySpan<byte> Key => _headerPayload.AsSpan(_header.ExtraLength, _header.KeyLength);

        /// <summary>
        /// return body from response
        /// </summary>
        /// <returns></returns>
        public Stream GetBody() => _stream;

        public ValueTask DisposeAsync()
        {
            if (_headerPayload != null)
                PoolBuffer.Return(_headerPayload);
            return _stream.DisposeAsync();
        }

        internal static async ValueTask<Response> FromStream(Stream payload, CancellationToken token)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(Protocol.HeaderSize);
            try
            {
                payload.SetLength(Protocol.HeaderSize);
                await payload.ReadExactAsync(buffer.AsMemory(0, Protocol.HeaderSize), token).ConfigureAwait(false);
                var header = Protocol.ReadHeader(buffer, out _);
                payload.SetLength(header.TotalSize + Protocol.HeaderSize);

                var headerPayloadSize = header.ExtraLength + header.KeyLength;

                byte[]? headerPayload = null;
                
                if (headerPayloadSize > 0)
                {
                    headerPayload = PoolBuffer.Rent(headerPayloadSize);
                    await payload.ReadExactAsync(headerPayload.AsMemory(0, headerPayloadSize), token).ConfigureAwait(false);
                }

                return new Response(header, headerPayload, payload);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}