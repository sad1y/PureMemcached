using System;
using System.Buffers.Binary;
using System.IO;

namespace PureMemcached.Protocol;

internal class BinaryProtocolReader
{
    private readonly Stream _stream;

    public BinaryProtocolReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

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

    internal unsafe Response Read()
    {
        const int headerSize = 24;
        var headerMem = stackalloc byte[headerSize];
        var headerSpan = new Span<byte>(headerMem, headerSize);

        // read into buffer header part
        if (_stream.Read(headerSpan) != headerSize)
            throw new IOException("corrupted stream. failed to read header");

        var header = new ResponseHeader(
            headerSpan[0],
            (OpCode)headerSpan[1],
            BinaryPrimitives.ReadUInt16BigEndian(headerSpan[2..]),
            headerSpan[4],
            headerSpan[5],
            (Status)BinaryPrimitives.ReadUInt16BigEndian(headerSpan[6..]),
            BinaryPrimitives.ReadUInt32BigEndian(headerSpan[8..]),
            BinaryPrimitives.ReadUInt32BigEndian(headerSpan[12..]),
            BinaryPrimitives.ReadUInt64BigEndian(headerSpan[16..])
        );

        if (header.Magic != 0x81) // sanity check
            throw new IOException("magic check failed");

        return new Response(header, _stream);
    }
}