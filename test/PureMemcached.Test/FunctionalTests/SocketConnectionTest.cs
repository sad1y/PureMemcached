using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace PureMemcached.Test.FunctionalTests;

public class SocketConnectionTest : ConnectionTestBase
{
    [Fact]
    public async Task TimeoutOnReceive_ShouldThrowException()
    {
        var answerBuffer = new byte[] { 1, 2, 3, 4 };
        var requestBuffer = new byte[] { 4, 3, 2, 1 };

        await StartEnv(new ServerMock.SendReceiveMock[]
            {
                new(answerBuffer, requestBuffer.Length, TimeSpan.FromMilliseconds(500))
            },
            async pool =>
            {
                var func = async () =>
                {
                    var connection = await pool.RentAsync();
                    var tcs = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                    var res = await connection.SendAsync(new MemoryStream(requestBuffer), tcs.Token);
                    res.SetLength(answerBuffer.Length);
                    var buffer = new byte[4];
                    await res.ReadAsync(buffer.AsMemory(), tcs.Token);    
                };
                
                await func.Should().ThrowExactlyAsync<OperationCanceledException>();
            });
    }

    [Fact]
    public async Task TrySend_BeforePreviousOperationComplete_ShouldThrowException()
    {
        var answerBuffer = new byte[] { 1, 2, 3, 4 };
        var requestBuffer = new byte[] { 4, 3, 2, 1 };

        await StartEnv(new ServerMock.SendReceiveMock[]
            {
                new(answerBuffer, requestBuffer.Length)
            },
            async pool =>
            {
                var func = async () =>
                {
                    var connection = await pool.RentAsync();
                    var firstRequestTask = connection.SendAsync(new MemoryStream(requestBuffer), CancellationToken.None);
                    var secondRequestTask = connection.SendAsync(new MemoryStream(requestBuffer), CancellationToken.None);
                    await Task.WhenAny(firstRequestTask, secondRequestTask);
                };
                await func.Should().ThrowExactlyAsync<MemcachedClientException>();
            });
    }
    
   
}