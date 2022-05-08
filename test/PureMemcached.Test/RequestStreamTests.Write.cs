using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using PureMemcached.Extensions;
using Xunit;

namespace PureMemcached.Test;

public class RequestStreamTests
{
    [Theory]
    [MemberData(nameof(GetRequests))]
    internal Task Write_ValidRequest_ShouldWriteExpectedBytesIntoOutput(RequestWrapper request, byte[] expected)
    {
        Request r = request;
        return Core(new RequestStream(ref r));

        async Task Core(Stream requestStream)
        {
            using var mem = new MemoryStream();
            await requestStream.CopyToAsync(mem);

            mem.GetBuffer().AsSpan(0, (int)mem.Length).ToArray().Should().BeEquivalentTo(expected);
        }
    }

    public static IEnumerable<object[]> GetRequests()
    {
        yield return new object[]
        {
            new RequestWrapper
            {
                Extra = ReadOnlyMemory<byte>.Empty,
                OpCode = OpCode.Get,
                Key = Encoding.UTF8.GetBytes("Hello"),
                Value = Stream.Null
            },
            new byte[]
            {
                0x80, 0, 0, 5,
                0, 0, 0, 0,
                0, 0, 0, 5,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0x48, 0x65, 0x6c, 0x6c, 0x6f
            }
        };

        yield return new object[]
        {
            new RequestWrapper
            {
                Extra = new byte[] { 2, 4, 6 },
                OpCode = OpCode.Get,
                Key = Encoding.UTF8.GetBytes("Hello"),
                Value = Stream.Null,
                Cas = 29999999991,
                Opaque = 3092
            },
            new byte[]
            {
                0x80, 0, 0, 5,
                3, 0, 0, 0,
                0, 0, 0, 8,
                0, 0, 12, 20,
                0, 0, 0, 6,
                252, 35, 171, 247,
                2, 4, 6,
                0x48, 0x65, 0x6c, 0x6c, 0x6f
            }
        };

        yield return new object[]
        {
            new RequestWrapper
            {
                Extra = new byte[] { 2, 4, 6 },
                OpCode = OpCode.Get,
                Key = Encoding.UTF8.GetBytes("Hello"),
                Value = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }),
                Cas = 29999999991,
                Opaque = 3092
            },
            new byte[]
            {
                0x80, 0, 0, 5,
                3, 0, 0, 0,
                0, 0, 0, 17,
                0, 0, 12, 20,
                0, 0, 0, 6,
                252, 35, 171, 247,
                2, 4, 6,
                0x48, 0x65, 0x6c, 0x6c, 0x6f,
                1, 2, 3, 4, 5, 6, 7, 8, 9
            }
        };
    }
}