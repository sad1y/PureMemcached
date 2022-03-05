using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PureMemcached.Test")]

namespace PureMemcached
{
    internal readonly struct BinaryProtocolWriter
    {
        private readonly Stream _stream;

        public BinaryProtocolWriter(Stream stream)
        {
            _stream = stream;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void WriteHeader(uint requestId, ulong cas, OpCode opCode, ushort keyLength, byte extraLength, uint totalLength)
        {
            /*
             * Request header:
        
             Byte/     0       |       1       |       2       |       3       |
                /              |               |               |               |
               |0 1 2 3 4 5 6 7|0 1 2 3 4 5 6 7|0 1 2 3 4 5 6 7|0 1 2 3 4 5 6 7|
               +---------------+---------------+---------------+---------------+
              0| Magic         | Opcode        | Key length                    |
               +---------------+---------------+---------------+---------------+
              4| Extras length | Data type     | Reserved                      |
               +---------------+---------------+---------------+---------------+
              8| Total body length                                             |
               +---------------+---------------+---------------+---------------+
             12| Opaque                                                        |
               +---------------+---------------+---------------+---------------+
             16| CAS                                                           |
               |                                                               |
               +---------------+---------------+---------------+---------------+
               Total 24 bytes
             */

            const int requestHeaderSize = 24;

            var headerBuffer = stackalloc byte[requestHeaderSize];
            var header = new Span<byte>(headerBuffer, requestHeaderSize);

            header[0] = 0x80; // request magic
            header[1] = (byte)opCode;
            BinaryPrimitives.WriteUInt16BigEndian(header[2..4], keyLength);

            header[4] = extraLength;
            header[5] = 0; // 0x00 since only raw bytes are supported 
            BinaryPrimitives.WriteUInt16BigEndian(header[6..8], 0); // we don't support reserved field 

            BinaryPrimitives.WriteUInt32BigEndian(header[8..12], totalLength + keyLength + extraLength);
            BinaryPrimitives.WriteUInt32BigEndian(header[12..16], requestId);
            BinaryPrimitives.WriteUInt64BigEndian(header[16..24], cas);

            _stream.Write(header);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ref Request request)
        {
            if (request.Key.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException($"key size should be less or eqaul to {ushort.MaxValue}");

            if (request.Extra.Length > byte.MaxValue)
                throw new ArgumentOutOfRangeException($"extra size should be less or eqaul to {byte.MaxValue}");

            if (request.Value is { Length: > uint.MaxValue })
                throw new ArgumentOutOfRangeException($"value size should be less or eqaul to {uint.MaxValue}");

            WriteHeader(
                request.RequestId,
                request.Cas, request.OpCode,
                (ushort)request.Key.Length,
                (byte)request.Extra.Length,
                (uint)(request.Value?.Length ?? 0));

            _stream.Write(request.Extra);
            _stream.Write(request.Key);

            request.Value?.CopyTo(_stream);
        }
    }
}