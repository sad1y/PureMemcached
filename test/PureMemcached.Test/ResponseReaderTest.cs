using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using PureMemcached.Protocol;
using Xunit;

namespace PureMemcached.Test
{
    public unsafe class ResponseReaderTest
    {
        [Fact]
        public void Read_OnEmptyStream_ShouldThrowException()
        {
            Assert.Throws<ArgumentNullException>(() => { new BinaryProtocolReader(null); });
        }

        [Fact]
        public void Read_OnStreamWithoutHeader_ShouldThrowException()
        {
            var reader = new BinaryProtocolReader(Stream.Null);

            Assert.Throws<IOException>(() => reader.Read());
        }

        [Fact]
        public void Read_OnStreamWithBrokenHeader_ShouldThrowException()
        {
            var input = new MemoryStream(new byte[32]);
            var reader = new BinaryProtocolReader(input);
            Assert.Throws<IOException>(() => reader.Read());
        }

        [Theory]
        [MemberData(nameof(GetValidResponses))]
        internal void Read_ValidResponse(Stream stream, ulong cas, uint requestId, Status status, OpCode opCode,
            byte[] expectedExtra, byte[] expectedKey, byte[] expectedValue)
        {
            var reader = new BinaryProtocolReader(stream);

            var response = reader.Read();

            byte[] ReadToEnd(ReadDelegate action)
            {
                var m = new byte[1024];
                var written = action(m);
                return m[..written];
            }

            Assert.Equal(cas, response.Cas);
            Assert.Equal(requestId, response.RequestId);
            Assert.Equal(status, response.Status);
            Assert.Equal(opCode, response.OpCode);
            Assert.Equal(expectedExtra, ReadToEnd(response.ReadExtra));
            Assert.Equal(expectedKey, ReadToEnd(response.ReadKey));
            Assert.Equal(expectedValue, ReadToEnd(response.ReadBody));
        }

        [Fact]
        internal void Read_InvalidMagic_ShouldThrowException()
        {
            var stream = new MemoryStream(new byte[]
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
            });


            this.Invoking(_ =>
            {
                var reader = new BinaryProtocolReader(stream);
                return reader.Read();
            }).Should().Throw<IOException>();
        }

        [Theory]
        [MemberData(nameof(GetInvalidResponses))]
        internal void Read_InvalidResponse(Stream stream, Status status)
        {
            var reader = new BinaryProtocolReader(stream);

            var response = reader.Read();
            response.Status.Should().Be(status);
        }

        private delegate int ReadDelegate(Span<byte> buffer);

        public static IEnumerable<object[]> GetValidResponses()
        {
            yield return new object[]
            {
                new MemoryStream(new byte[]
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
                }),
                1UL, 0U, Status.NoError, OpCode.Get,
                new byte[] { 0xde, 0xad, 0xbe, 0xef },
                Array.Empty<byte>(),
                new byte[] { 0x57, 0x6f, 0x72, 0x6c, 0x64 }
            };
        }

        public static IEnumerable<object[]> GetInvalidResponses()
        {
            yield return new object[]
            {
                new MemoryStream(new byte[]
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
                }),
                Status.ItemNotStored
            };
            yield return new object[]
            {
                new MemoryStream(new byte[]
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
                }),
                Status.KeyNotFound
            };
            yield return new object[]
            {
                new MemoryStream(new byte[]
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
                }),
                Status.KeyExists
            };
            
            yield return new object[]
            {
                new MemoryStream(new byte[]
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
                }),
                Status.ValueTooLarge
            };
            
            yield return new object[]
            {
                new MemoryStream(new byte[]
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
                }),
                Status.InvalidArguments
            };
            
            yield return new object[]
            {
                new MemoryStream(new byte[]
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
                }),
                Status.IncrDecrOnNonNumericValue
            };
            
            yield return new object[]
            {
                new MemoryStream(new byte[]
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
                }),
                Status.UnknownCommand
            };
            
            yield return new object[]
            {
                new MemoryStream(new byte[]
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
                }),
                Status.OutOfMemory
            };
        }
    }
}