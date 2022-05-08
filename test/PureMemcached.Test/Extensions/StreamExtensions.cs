using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using PureMemcached.Extensions;
using Xunit;

namespace PureMemcached.Test.Extensions;

public class StreamExtensionsTests
{
    [Fact]
    public async Task ReadExactAsync_ShouldCopyOnlyRequestPortionOfData()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0 });
        var buffer = new byte[5];

        await stream.ReadExactAsync(buffer, CancellationToken.None);

        buffer.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5 });
        stream.Position.Should().Be(5);
    }
}