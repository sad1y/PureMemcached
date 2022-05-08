using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace PureMemcached.Test;

public partial class ProtocolTests
{
    [Fact]
    public void Read_OnEmptyStream_ShouldThrowException()
    {
        var func = () => Protocol.ReadHeader(ReadOnlySpan<byte>.Empty, out _);
        func.Should().ThrowExactly<MemcachedClientException>();
    }

    [Fact]
    public void Read_OnStreamWithBrokenHeader_ShouldThrowException()
    {
        var input = new byte[32];
        var func = () => Protocol.ReadHeader(input, out _);
        func.Should().ThrowExactly<MemcachedClientException>();
    }

    [Theory]
    [MemberData(nameof(GetValidResponses))]
    internal void Read_ValidResponse(byte[] buffer, ulong cas, uint opaque, Status status, OpCode opCode,
        byte expectedExtraLength, byte expectedKeyLength, uint totalBodyLength )
    {
        var header = Protocol.ReadHeader(buffer, out _);

        header.Cas.Should().Be(cas);
        header.Opaque.Should().Be(opaque);
        header.Status.Should().Be(status);
        header.OpCode.Should().Be(opCode);
        header.ExtraLength.Should().Be(expectedExtraLength);
        header.KeyLength.Should().Be(expectedKeyLength);
        header.TotalSize.Should().Be(totalBodyLength);
    }

    [Fact]
    internal void Read_InvalidMagic_ShouldThrowException()
    {
        var response = new byte[]
        {
            0x85, 0x00, 0x00, 0x00,
            0x04, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x09,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x01,
            0xde, 0xad, 0xbe, 0xef,
            0x57, 0x6f, 0x72, 0x6c,
            0x64
        };

        var func = () => Protocol.ReadHeader(response, out _);
        func.Should().Throw<MemcachedClientException>();
    }

    [Theory]
    [MemberData(nameof(GetInvalidResponses))]
    internal void Read_InvalidResponse(byte[] chunk, Status status)
    {
        var response = Protocol.ReadHeader(chunk, out _);
        response.Status.Should().Be(status);
    }

    public static IEnumerable<object[]> GetValidResponses()
    {
        yield return new object[]
        {
            new byte[]
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
            },
            1UL, 0U, Status.NoError, OpCode.Get,
            4, 0, 9
        };
    }

    public static IEnumerable<object[]> GetInvalidResponses()
    {
        yield return new object[]
        {
            new byte[]
            {
                0x81, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x05,
                0x00, 0x00, 0x00, 0x09,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x01,
                0xde, 0xad, 0xbe, 0xef,
                0x57, 0x6f, 0x72, 0x6c,
                0x64
            },
            Status.ItemNotStored
        };
        yield return new object[]
        {
            new byte[]
            {
                0x81, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x09,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x01,
                0xde, 0xad, 0xbe, 0xef,
                0x57, 0x6f, 0x72, 0x6c,
                0x64
            },
            Status.KeyNotFound
        };
        yield return new object[]
        {
            new byte[]
            {
                0x81, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x02,
                0x00, 0x00, 0x00, 0x09,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x01,
                0xde, 0xad, 0xbe, 0xef,
                0x57, 0x6f, 0x72, 0x6c,
                0x64
            },
            Status.KeyExists
        };

        yield return new object[]
        {
            new byte[]
            {
                0x81, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x03,
                0x00, 0x00, 0x00, 0x09,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x01,
                0xde, 0xad, 0xbe, 0xef,
                0x57, 0x6f, 0x72, 0x6c,
                0x64
            },
            Status.ValueTooLarge
        };

        yield return new object[]
        {
            new byte[]
            {
                0x81, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x04,
                0x00, 0x00, 0x00, 0x09,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x01,
                0xde, 0xad, 0xbe, 0xef,
                0x57, 0x6f, 0x72, 0x6c,
                0x64
            },
            Status.InvalidArguments
        };

        yield return new object[]
        {
            new byte[]
            {
                0x81, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x06,
                0x00, 0x00, 0x00, 0x09,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x01,
                0xde, 0xad, 0xbe, 0xef,
                0x57, 0x6f, 0x72, 0x6c,
                0x64
            },
            Status.IncrDecrOnNonNumericValue
        };

        yield return new object[]
        {
            new byte[]
            {
                0x81, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x51,
                0x00, 0x00, 0x00, 0x09,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x01,
                0xde, 0xad, 0xbe, 0xef,
                0x57, 0x6f, 0x72, 0x6c,
                0x64
            },
            Status.UnknownCommand
        };

        yield return new object[]
        {
            new byte[]
            {
                0x81, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x52,
                0x00, 0x00, 0x00, 0x09,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x01,
                0xde, 0xad, 0xbe, 0xef,
                0x57, 0x6f, 0x72, 0x6c,
                0x64
            },
            Status.OutOfMemory
        };
    }
}