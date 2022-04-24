using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;

namespace PureMemcached.Test;

public partial class MemcachedClientTest
{
    [Fact]
    public async Task Get_FailedToReadResponse_ShouldThrowException()
    {
        await using var client = CreateClient(new[] { BrokenStream() });
        var act = async () => { await client.Get(new byte[] { 1 }, 2, 3, CancellationToken.None); };
        await act.Should().ThrowAsync<IOException>();
    }

    [Theory]
    [MemberData(nameof(GetOperationCases))]
    public async Task Get_ShouldProperlyReadResponse(uint requestId, ulong cas, byte[] key, byte[] extra, byte[] body)
    {
        await using var client = CreateClient(new[] { new StreamWrapper(CreateResponse(requestId, cas, OpCode.Get, key, extra, body)) });
        var response = await client.Get(new byte[] { 1 }, 2, 3, CancellationToken.None);

        var writer = new ArrayBufferWriter<byte>(256);

        response.Cas.Should().Be(cas);
        response.RequestId.Should().Be(requestId);
        response.Status.Should().Be(Status.NoError);
        await response.CopyExtraAsync(writer, CancellationToken.None);
        writer.WrittenMemory.ToArray().Should().BeEquivalentTo(extra);

        writer.Clear();
        await response.CopyKeyAsync(writer, CancellationToken.None);
        writer.WrittenMemory.ToArray().Should().BeEquivalentTo(key);

        var memoryStream = new MemoryStream();
        await response.GetStream().CopyToAsync(memoryStream);
        
        memoryStream.GetBuffer()[..body.Length].Should().BeEquivalentTo(body);
        response.GetStream().Length.Should().Be(response.GetStream().Position);
    }

    public static IEnumerable<object[]> GetOperationCases()
    {
        yield return
            new object[] { (uint)1, (ulong)2, new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3, 4 }, new byte[] { 3, 5, 6, 7, 91, 0 } };
    }

    private static MemcachedClient CreateClient(IEnumerable<Stream> responses = null)
    {
        var poolMock = new Mock<IObjectPool<Connection>>();
        var connectionMock = new Mock<Connection>(null);

        var iterator = (responses ?? Enumerable.Empty<Stream>()).GetEnumerator();

        connectionMock.Setup(f => f.SendAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => iterator.MoveNext() ? iterator.Current : Stream.Null);

        poolMock.Setup(f => f.RentAsync()).ReturnsAsync(connectionMock.Object);

        return new MemcachedClient(poolMock.Object);
    }

    private static Stream BrokenStream()
    {
        var mock = new Mock<Stream>();
        mock.Setup(f => f.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => throw new IOException());
        return mock.Object;
    }

    private static byte[] CreateResponse(uint requestId, ulong cas, OpCode code, byte[] key, byte[] extra, byte[]? body)
    {
        var totalLength = key.Length + extra.Length + body?.Length ?? 0;
        var len = Protocol.HeaderSize + totalLength;
        var buffer = new byte[len];

        var span = buffer.AsSpan();

        buffer[0] = 0x81;
        buffer[1] = (byte)code;
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)key.Length);
        buffer[4] = (byte)extra.Length;
        // skip data_type and reserved
        BinaryPrimitives.WriteUInt32BigEndian(span[8..12], (uint)totalLength);
        BinaryPrimitives.WriteUInt32BigEndian(span[12..16], requestId);
        BinaryPrimitives.WriteUInt64BigEndian(span[16..], cas);

        var offset = Protocol.HeaderSize;
        extra.CopyTo(span[offset..]);
        offset += extra.Length;
        key.CopyTo(span[offset..]);
        offset += key.Length;
        body.CopyTo(span[offset..]);

        return buffer;
    }
}