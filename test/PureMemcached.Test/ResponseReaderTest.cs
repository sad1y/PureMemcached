using System;
using System.Collections.Generic;
using System.IO;

using Xunit;

namespace PureMemcached.Test
{
    public unsafe class ResponseReaderTest
    {
        private static TestResponse CreateResponse(ResponseHeader header, Stream body) =>
            new(header, body);

        [Fact]
        public void Read_OnEmptyStream_ShouldThrowException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                new BinaryProtocolReader(null);
            });
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
        [MemberData(nameof(GetResponses))]
        internal void Read_ValidResponse(Stream stream, ulong cas, uint requestId, Status status, OpCode opCode,
            byte[] expectedExtra, byte[] expectedKey, byte[] expectedValue)
        {
            var reader = new BinaryProtocolReader(stream);

            var response = reader.Read();

            byte[] ReadToEnd(ReadDelegate action)
            {
                var m = new MemoryStream(1024); 
                m.Position = action(m.GetBuffer());
                return m.ToArray();
            }

            Assert.Equal(cas, response.Cas);
            Assert.Equal(requestId, response.RequestId);
            Assert.Equal(status, response.Status);
            Assert.Equal(opCode, response.OpCode);
            Assert.Equal(expectedExtra, ReadToEnd(response.ReadExtra));
            Assert.Equal(expectedKey, ReadToEnd(response.ReadKey));
            Assert.Equal(expectedValue, ReadToEnd(response.ReadBody));
        }

        private delegate int ReadDelegate(Span<byte> buffer);
        
        private class TestResponse : Response
        {
            public TestResponse(ResponseHeader header, Stream body) : base(header, body)
            {
            }

            public byte[] GetExtra()
            {
                var buffer = new byte[Header.ExtraLength];
                ReadExtra(buffer);
                return buffer;
            }

            public byte[] GetKey()
            {
                var buffer = new byte[Header.KeyLength];
                ReadKey(buffer);
                return buffer;
            }
            
            public byte[] GetValue()
            {
                var buffer = new byte[Header.TotalSize - (Header.KeyLength + Header.ExtraLength)];
                ReadBody(buffer);
                return buffer;
            }
        }

        public static IEnumerable<object[]> GetResponses()
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
    }
}