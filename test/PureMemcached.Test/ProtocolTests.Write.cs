using System;
using FluentAssertions;
using Xunit;

namespace PureMemcached.Test;

public partial class ProtocolTests
{
    [Fact]
    internal void Write_OnLargeKey_ShouldThrowException()
    {
        var func = () =>
        {
            var buffer = new byte[512];
            var request = new Request
            {
                Key = new ReadOnlySpan<byte>(new byte[Protocol.MaxKeyLength + 1])
            };
            Protocol.WriteHeader(ref request, buffer, out _);
        };

        func.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    internal void Write_OnLargeExtra_ShouldThrowException()
    {
        var func = () =>
        {
            var buffer = new byte[512];
            var request = new Request
            {
                Extra = new ReadOnlySpan<byte>(new byte[byte.MaxValue + 1])
            };
            Protocol.WriteHeader(ref request, buffer, out _);
        };
        
        func.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }
    
  
}