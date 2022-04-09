using System;
using System.Buffers.Text;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace PureMemcached
{
    public class Response : IDisposable
    {
        private readonly ResponseHeader _header;
        private readonly Stream _body;
        private uint _offset;
        public bool InvalidCas { get; private set; }

        public Status Status => _header.Status;
        
        public ulong Cas => _header.Cas;

        public uint RequestId => _header.RequestId;
        
        public uint KeyLength => _header.KeyLength;
        
        public long BodyLength => _header.TotalSize - (_header.KeyLength + _header.ExtraLength);
        
        public uint ExtraLength => _header.ExtraLength;
        
        public OpCode OpCode => _header.OpCode;
        internal long TotalSize => _header.TotalSize;

        internal Response(ResponseHeader header, Stream body)
        {
            _header = header;
            _body = body;
            _offset = 0; // because we had already read header
        }

        internal void VerifyCas(ulong cas)
        {
            InvalidCas = cas != 0 && Cas != cas;
        }

        public bool HasError() => InvalidCas || Status != Status.NoError; 
        
        public string ReadErrorAsString()
        {
            var otherLen = (_header.ExtraLength + _header.KeyLength);

            if (_offset > otherLen)
                throw new IOException("cannot read error");
            
            var len = (int)_header.TotalSize - otherLen;

            Span<byte> errorBuffer = stackalloc byte[len];
            
            // move strait to error message, and skip anything 
            if (_offset < otherLen)
                _body.Read(errorBuffer);

            var read = _body.Read(errorBuffer);
            var offset = 0;
            
            while (read > 0 && read + offset < len)
            {
                offset += read; 
                read = _body.Read(errorBuffer[offset..]);
            }

            return Encoding.UTF8.GetString(errorBuffer);
        }
        
        /// <summary>
        /// read textual error from response. should be used only if GetStatus return non-zero status   
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns>bytes read</returns>
        public int ReadError(Span<byte> buffer) =>
            Read(buffer, _header.TotalSize);

        /// <summary>
        /// read extra from response. should be used first if `HasError` equals false   
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns>bytes read</returns>
        public int ReadExtra(Span<byte> buffer) =>
            Read(buffer, _header.ExtraLength);

        /// <summary>
        /// read key from response. should be used after `ReadExtra` method   
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns>bytes read</returns>
        public int ReadKey(Span<byte> buffer) =>
            Read(buffer, _header.KeyLength);

        /// <summary>
        /// read value from response   
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns>bytes read</returns>
        public int ReadBody(Span<byte> buffer)
        {
            if (_header.KeyLength + _header.ExtraLength > _offset)
                throw new IOException("Response contains key or extra data. You should read it first");

            return Read(buffer, _header.TotalSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Read(Span<byte> buffer, uint limit)
        {
            if (limit == 0) return 0;

            var left = (int)(limit - _offset);
            buffer = buffer.Length > left ? buffer[..left] : buffer;
            var read = _body.Read(buffer);
            _offset += (uint)read;
            return read;
        }

        public void Dispose()
        {
            _body.Dispose();
        }
    }
}