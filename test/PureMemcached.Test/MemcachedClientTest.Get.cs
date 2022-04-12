using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using PureMemcached.Protocol;
using Xunit;

namespace PureMemcached.Test;

public partial class MemcachedClientTest
{
    [Fact]
    public async Task Get_FromSeveralRead_ShouldReturnCompleteResponse()
    {
        var key = new byte[] { 0, 2, 6, 1, 8 };
        var requestByte = CreateRequestBuffer(new Request { RequestId = 2, Cas = 3, Key = key, OpCode = OpCode.Get });
        var responseByte = new byte[]
        {
            0x81, 0x00, 0x00, 0x00,
            0x04, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x09,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x01,
            0xde, 0xad, 0xbe, 0xef,
            0x57, 0x6f, 0x72, 0x6c,
            0x64
        };

        var expectedFlow = new ExecutionFlow
        {
            new BeginSendState(requestByte, 0, 29),
            new CompleteState(6),
            new BeginSendState(requestByte[6..], 6, 23),
            new CompleteState(23),
            new BeginReceiveState(0, 4096),
            new CompleteReceiveState(1, responseByte[..1]),
            new BeginReceiveState(0, 4096),
            new CompleteReceiveState(10, responseByte[1..11]),
            new BeginReceiveState(0, 4096),
            new CompleteReceiveState(7, responseByte[11..18]),
            new BeginReceiveState(0, 4096),
            new CompleteReceiveState(15, responseByte[18..]),
        };

        await using var client = new MemcachedClient(new MockConnection(expectedFlow), blockSize: 128);

        var response = await client.Get(key, 2, 3, CancellationToken.None);

        response.Status.Should().Be(Status.NoError);

        var buffer = new byte[16];

        var keyLength = response.ReadKey(buffer);
        keyLength.Should().Be(0);

        var extraLength = response.ReadExtra(buffer);
        extraLength.Should().Be(4);
        buffer[..extraLength].Should().BeEquivalentTo(new byte[] { 0xde, 0xad, 0xbe, 0xef });

        var bodyLength = response.ReadBody(buffer);
        bodyLength.Should().Be(5);
        buffer[..bodyLength].Should().BeEquivalentTo(new byte[] { 0x57, 0x6f, 0x72, 0x6c, 0x64 });

        expectedFlow.AssertThatThereIsNoStatesLeft();
    }

    [Fact]
    public async Task Get_CallAgainBeforeClientReady_ShouldThrowException()
    {
        await using var client = new MemcachedClient(Mock.Of<Connection>(), blockSize: 128);
        client.Get(CreateRequestBuffer(new Request()));
        
        var act = () => { client.Get(CreateRequestBuffer(new Request())); };
        
        act.Should().Throw<MemcachedClientException>();
    }
    
    [Fact]
    public async Task Get_FailedToSendRequest_ShouldThrowException()
    {
        var key = new byte[] { 0, 2, 6, 1, 8 };

        var expectedFlow = new ExecutionFlow
        {
            new ThrowsExceptionState()
        };

        await using var client = new MemcachedClient(new MockConnection(expectedFlow), blockSize: 128);

        var act = async () => { await client.Get(key, 2, 3, CancellationToken.None); };

        await act.Should().ThrowAsync<MemcachedClientException>();

        expectedFlow.AssertThatThereIsNoStatesLeft();
    }

    [Fact]
    public async Task Get_FailedToReadResponse_ShouldThrowException()
    {
        var key = new byte[] { 0, 2, 6, 1, 8 };
        var requestByte = CreateRequestBuffer(new Request { RequestId = 2, Cas = 3, Key = key, OpCode = OpCode.Get });

        var expectedFlow = new ExecutionFlow
        {
            new BeginSendState(requestByte, 0, 29),
            new CompleteState(29),
            new BeginReceiveState(0, 4096),
            new ThrowsExceptionState()
        };

        await using var client = new MemcachedClient(new MockConnection(expectedFlow), blockSize: 128);

        var act = async () => { await client.Get(key, 2, 3, CancellationToken.None); };

        await act.Should().ThrowAsync<MemcachedClientException>();

        expectedFlow.AssertThatThereIsNoStatesLeft();
    }

    private static byte[] CreateRequestBuffer(Request request)
    {
        using var mem = new MemoryStream();

        var writer = new BinaryProtocolWriter(mem);
        writer.Write(ref request);

        return mem.ToArray();
    }
}