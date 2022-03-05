using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

// ReSharper disable HeapView.BoxingAllocation

namespace PureMemcached.Test
{
    public class RequestWriterTest
    {
        [Theory]
        [MemberData(nameof(GetRequests))]
        internal void Write_ValidRequest_ShouldWriteExpectedBytesIntoOutput(RequestWrapper request, MemoryStream expected)
        {
            var mem = new MemoryStream(1000);
            var writer = new BinaryProtocolWriter(mem);

            Request r = request;
            writer.Write(ref r);
            mem.Position = 0;

            Assert.Equal(expected.ToArray(), mem.ToArray());
        }

        [Fact]
        internal void Write_OnLargeKey_ShouldThrowException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var mem = new MemoryStream(1000);
                var writer = new BinaryProtocolWriter(mem);
                var request = new Request
                {
                    Key = new ReadOnlySpan<byte>(new byte[ushort.MaxValue + 1])
                };
                writer.Write(ref request);
            });
        }

        [Fact]
        internal void Write_OnLargeExtra_ShouldThrowException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                var mem = new MemoryStream(1000);
                var writer = new BinaryProtocolWriter(mem);
                var request = new Request
                {
                    Extra = new ReadOnlySpan<byte>(new byte[byte.MaxValue + 1])
                };

                writer.Write(ref request);
            });
        }

        internal struct RequestWrapper
        {
            public uint RequestId { get; set; }

            public ulong Cas { get; set; }

            public OpCode OpCode { get; set; }

            public ReadOnlyMemory<byte> Key { get; set; }

            public ReadOnlyMemory<byte> Extra { get; set; }

            public Stream Value { get; set; }
            
            public static implicit operator Request(RequestWrapper w)
            {
                return new Request
                {
                    RequestId = w.RequestId,
                    Cas = w.Cas,
                    OpCode = w.OpCode,
                    Key = w.Key.Span,
                    Extra = w.Extra.Span,
                    Value = w.Value
                };
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
                new MemoryStream(new byte[]
                {
                    0x80, 0, 0, 5,
                    0, 0, 0, 0,
                    0, 0, 0, 5,
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0x48, 0x65, 0x6c, 0x6c, 0x6f
                })
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
                    RequestId = 3092
                },
                new MemoryStream(new byte[]
                {
                    0x80, 0, 0, 5,
                    3, 0, 0, 0,
                    0, 0, 0, 8,
                    0, 0, 12, 20,
                    0, 0, 0, 6,
                    252, 35, 171, 247,
                    2, 4, 6,
                    0x48, 0x65, 0x6c, 0x6c, 0x6f
                })
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
                    RequestId = 3092
                },
                new MemoryStream(new byte[]
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
                })
            };
        }
    }
}