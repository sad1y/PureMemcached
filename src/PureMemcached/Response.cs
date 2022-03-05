using System;
using System.Buffers.Text;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace PureMemcached
{
    public class Response : IDisposable
    {
        protected readonly ResponseHeader Header;
        protected readonly Stream Body;
        private uint _offset;
        public bool InvalidCas { get; private set; }

        public Status Status => Header.Status;
        
        public ulong Cas => Header.Cas;

        public uint RequestId => Header.RequestId;
        
        public uint KeyLength => Header.KeyLength;
        
        public long BodyLength => Header.TotalSize - (Header.KeyLength + Header.ExtraLength);
        
        public uint ExtraLength => Header.ExtraLength;
        
        public OpCode OpCode => Header.OpCode;
        internal long TotalSize => Header.TotalSize;

        internal Response(ResponseHeader header, Stream body)
        {
            Header = header;
            Body = body;
            _offset = 0; // because we had already read header
        }

        internal void VerifyCas(ulong cas)
        {
            InvalidCas = cas != 0 && Cas != cas;
        }

        public bool HasError() => InvalidCas || Status != Status.NoError; 
        
        public string ReadErrorAsString()
        {
            var otherLen = (Header.ExtraLength + Header.KeyLength);

            if (_offset > otherLen)
                throw new IOException("cannot read error");
            
            var len = (int)Header.TotalSize - otherLen;

            Span<byte> errorBuffer = stackalloc byte[len];
            
            // move strait to error message, and skip anything 
            if (_offset < otherLen)
                Body.Read(errorBuffer);

            var read = Body.Read(errorBuffer);
            var offset = 0;
            
            while (read > 0 && read + offset < len)
            {
                offset += read; 
                read = Body.Read(errorBuffer[offset..]);
            }

            return Encoding.UTF8.GetString(errorBuffer);
        }
        
        /// <summary>
        /// read textual error from response. should be used only if GetStatus return non-zero status   
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns>bytes read</returns>
        public int ReadError(Span<byte> buffer) =>
            Read(buffer, Header.TotalSize);

        /// <summary>
        /// read extra from response. should be used first if `HasError` equals false   
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns>bytes read</returns>
        public int ReadExtra(Span<byte> buffer) =>
            Read(buffer, Header.ExtraLength);

        /// <summary>
        /// read key from response. should be used after `ReadExtra` method   
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns>bytes read</returns>
        public int ReadKey(Span<byte> buffer) =>
            Read(buffer, Header.KeyLength);

        /// <summary>
        /// read value from response   
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns>bytes read</returns>
        public int ReadBody(Span<byte> buffer)
        {
            if (Header.KeyLength + Header.ExtraLength > _offset)
                throw new IOException("Response contains key or extra data. You should read it first");

            return Read(buffer, Header.TotalSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Read(Span<byte> buffer, uint limit)
        {
            if (limit == 0) return 0;

            var left = (int)(limit - _offset);
            buffer = buffer.Length > left ? buffer[..left] : buffer;
            var read = Body.Read(buffer);
            _offset += (uint)read;
            return read;
        }

        public void Dispose()
        {
            Body.Dispose();
        }
    }
}