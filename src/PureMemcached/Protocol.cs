using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PureMemcached.Test")]

namespace PureMemcached;

internal static class Protocol
{
    public const int HeaderSize = 24;
    public const int MaxKeyLength = 250;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteHeader(uint requestId, ulong cas, OpCode opCode, ushort keyLength, byte extraLength, uint totalLength,
        Span<byte> dest)
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

        dest[0] = 0x80; // request magic
        dest[1] = (byte)opCode;
        BinaryPrimitives.WriteUInt16BigEndian(dest[2..4], keyLength);

        dest[4] = extraLength;
        dest[5] = 0; // 0x00 since only raw bytes are supported 
        BinaryPrimitives.WriteUInt16BigEndian(dest[6..8], 0); // we don't support reserved field 

        BinaryPrimitives.WriteUInt32BigEndian(dest[8..12], totalLength + keyLength + extraLength);
        BinaryPrimitives.WriteUInt32BigEndian(dest[12..16], requestId);
        BinaryPrimitives.WriteUInt64BigEndian(dest[16..24], cas);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteHeader(ref Request request, Span<byte> dest, out int written)
    {
        if (request.Key.Length > MaxKeyLength)
            throw new ArgumentOutOfRangeException($"key size should be less or eqaul to {MaxKeyLength}");

        if (request.Key.Length == 0)
            throw new ArgumentOutOfRangeException($"key size shouldn't be eqaul to zero");

        if (request.Extra.Length > byte.MaxValue)
            throw new ArgumentOutOfRangeException($"extra size should be less or eqaul to {byte.MaxValue}");

        if (request.Payload is { Length: > uint.MaxValue })
            throw new ArgumentOutOfRangeException($"value size should be less or eqaul to {uint.MaxValue}");

        if (dest.Length < HeaderSize)
            throw new OutOfMemoryException("there is no space to write header");

        WriteHeader(
            request.RequestId,
            request.Cas, request.OpCode,
            (ushort)request.Key.Length,
            (byte)request.Extra.Length,
            (uint)(request.Payload?.Length ?? 0), dest);

        written = HeaderSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ResponseHeader ReadHeader(ReadOnlySpan<byte> chunk, out int read)
    {
                /*
        Response header:

         Byte/     0       |       1       |       2       |       3       |
            /              |               |               |               |
           |0 1 2 3 4 5 6 7|0 1 2 3 4 5 6 7|0 1 2 3 4 5 6 7|0 1 2 3 4 5 6 7|
           +---------------+---------------+---------------+---------------+
          0| Magic         | Opcode        | Key Length                    |
           +---------------+---------------+---------------+---------------+
          4| Extras length | Data type     | Status                        |
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

        // read into buffer header part
        if (chunk.Length < HeaderSize)
            throw new MemcachedClientException("Corrupted stream. Failed to read header");

        var header = new ResponseHeader(
            chunk[0],
            (OpCode)chunk[1],
            BinaryPrimitives.ReadUInt16BigEndian(chunk[2..]),
            chunk[4],
            chunk[5],
            (Status)BinaryPrimitives.ReadUInt16BigEndian(chunk[6..]),
            BinaryPrimitives.ReadUInt32BigEndian(chunk[8..]),
            BinaryPrimitives.ReadUInt32BigEndian(chunk[12..]),
            BinaryPrimitives.ReadUInt64BigEndian(chunk[16..])
        );

        read = HeaderSize;

        if (header.Magic != 0x81) 
            throw new MemcachedClientException("Sanity check failed");

        return header;
    }
}