using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;

namespace PureMemcached.Test;

public class MemcachedClientPoolTest
{
    [Fact]
    public async Task RentAsync_AfterReturn_ShouldReuseClient()
    {
        var connection = new Mock<Connection>();
        connection.Setup(f => f.IsReady).Returns(true);
        var connectionFactory = new Mock<IConnectionFactory>();
        connectionFactory.Setup(f => f.CreateAsync()).ReturnsAsync(connection.Object);

        using var pool = new MemcachedClientPool(connectionFactory.Object);

        var firstRentedClient = await pool.RentAsync();
        await firstRentedClient.DisposeAsync();

        await using var secondRentedClient = await pool.RentAsync();

        secondRentedClient.Should().Be(firstRentedClient);
    }

    [Fact]
    public async Task ReturnAsync_NotReadyClient_ShouldNotReuseIt()
    {
        var connection = new Mock<Connection>();
        connection.Setup(f => f.IsReady).Returns(false);
        var connectionFactory = new Mock<IConnectionFactory>();
        connectionFactory.Setup(f => f.CreateAsync()).ReturnsAsync(connection.Object);

        using var pool = new MemcachedClientPool(connectionFactory.Object);

        var firstRentedClient = await pool.RentAsync();
        await firstRentedClient.DisposeAsync();

        var secondRentedClient = await pool.RentAsync();

        secondRentedClient.Should().NotBe(firstRentedClient);
    }
    
    [Fact]
    public async Task RentAsync_HaveReadyClientThatNotReady_ShouldReleaseIt()
    {
        var connection = new Mock<Connection>();
        connection.Setup(f => f.IsReady).Returns(true);
        
        var connectionFactory = new Mock<IConnectionFactory>();
        connectionFactory.Setup(f => f.CreateAsync()).ReturnsAsync(connection.Object);

        using var pool = new MemcachedClientPool(connectionFactory.Object);

        await using var client = await pool.RentAsync();
        client.Release();
        await pool.ReturnAsync(client);
        
        await using var nextClient = await pool.RentAsync();

        nextClient.IsReady().Should().BeTrue();
        nextClient.Should().NotBe(client);
    }
}