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
        private static readonly byte[] DevNull = new byte[256];

        private readonly ResponseHeader _header;
        private readonly Stream _stream;

        public bool InvalidCas { get; private set; }

        public Status Status => _header.Status;

        public ulong Cas => _header.Cas;

        public uint RequestId => _header.RequestId;

        public uint KeyLength => _header.KeyLength;

        public long BodyLength => _header.TotalSize - (_header.KeyLength + _header.ExtraLength);

        public uint ExtraLength => _header.ExtraLength;

        public OpCode OpCode => _header.OpCode;
        internal long TotalSize => _header.TotalSize;

        internal Response(ResponseHeader header, Stream stream)
        {
            _header = header;
            _stream = stream;
        }

        internal void VerifyCas(ulong cas)
        {
            InvalidCas = cas != 0 && Cas != cas;
        }

        public bool HasError() => InvalidCas || Status != Status.NoError;

        public ValueTask<int> SkipKeyAsync(CancellationToken token) =>
            Skip(_header.KeyLength + _header.ExtraLength - (int)(_stream.Position - Protocol.HeaderSize), token);

        public ValueTask<int> SkipExtraAsync(CancellationToken token) =>
            Skip(_header.ExtraLength - (int)(_stream.Position - Protocol.HeaderSize), token);

        private ValueTask<int> Skip(int size, CancellationToken token)
        {
            return size <= 0 ? new ValueTask<int>(0) : _stream.ReadAsync(DevNull.AsMemory(0, size), token);
        }

        /// <summary>
        /// return raw stream from response
        /// </summary>
        /// <returns></returns>
        public Stream GetStream() => _stream;

        /// <summary>
        /// read textual error from response. should be used only if GetStatus return non-zero status   
        /// </summary>
        /// <param name="writer"></param>
        /// <returns>bytes read</returns>
        public async Task CopyErrorAsync(IBufferWriter<byte> writer, CancellationToken token)
        {
            await SkipKeyAsync(token);
            await _stream.CopyTo(writer, BodyLength, token);
        }

        /// <summary>
        /// read extra from response. should be used first if `HasError` equals false   
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="token"></param>
        /// <returns>bytes read</returns>
        public async Task CopyExtraAsync(IBufferWriter<byte> writer, CancellationToken token)
        {
            await _stream.CopyTo(writer, ExtraLength, token);
        }

        /// <summary>
        /// read key from response. should be used after `CopyExtraAsync` method   
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="token"></param>
        /// <returns>bytes read</returns>
        public async Task CopyKeyAsync(IBufferWriter<byte> writer, CancellationToken token)
        {
            await SkipExtraAsync(token);
            await _stream.CopyTo(writer, KeyLength, token);
        }

        public ValueTask DisposeAsync() => _stream.DisposeAsync();
    }
}