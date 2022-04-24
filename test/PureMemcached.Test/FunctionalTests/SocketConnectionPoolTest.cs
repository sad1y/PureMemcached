using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace PureMemcached.Test.FunctionalTests;

public class SocketConnectionPoolTest : ConnectionTestBase
{
    [Fact]
    public async Task RentAsync_AfterReturn_ShouldReuseClient()
    {
        await StartEnv(new ServerMock.SendReceiveMock[] { new(new byte[1], 1) }, async pool =>
        {
             var firstConnection = await pool.RentAsync();
             firstConnection.IsReady.Should().BeTrue();
             
             await pool.ReturnAsync(firstConnection);
             
             var secondConnection = await pool.RentAsync();
             secondConnection.Should().Be(firstConnection);
        });
    }
    
    [Fact]
    public async Task ReturnAsync_NotReadyClient_ShouldNotReuseIt()
    {
        await StartEnv(new ServerMock.SendReceiveMock[] { new(new byte[1], 1) }, async pool =>
        {
            var firstConnection = await pool.RentAsync();
            await using var res = await firstConnection.SendAsync(new MemoryStream(new byte[] { 1 }), CancellationToken.None);
            await pool.ReturnAsync(firstConnection);
            var secondConnection = await pool.RentAsync();
            secondConnection.Should().NotBe(firstConnection);
        });
    }
}