using System;
using System.Buffers;
using Xunit;

namespace PureMemcached.Test
{
    public class MemorySegmentStreamTest
    {
        [Fact]
        public void Read_OnNonInitializedStream()
        {
            var stream = new MemorySegmentStream(ArrayPool<byte>.Shared);
            var buffer = new byte[8];
            var read = stream.Read(buffer);

            Assert.Equal(0, read);
            Assert.Equal(0, stream.Length);
        }

        [Fact]
        public void Write_DataThatExceedSegmentSize()
        {
            var stream = new MemorySegmentStream(ArrayPool<byte>.Shared, 16);
            var buffer = new byte[128];

            var rnd = new Random();
            rnd.NextBytes(buffer);
            
            stream.Write(buffer);

            var result = new byte[128];
            var read = stream.Read(result);

            Assert.Equal(buffer.Length, read);
            Assert.Equal(buffer, result);

            var offset = 0;
            
            for (var i = 0; i < stream.SegmentCount; i++)
            {
                if (!stream.TryGetBuffer(out var block, i)) continue;
                
                var b = block.ToArray();
                    
                Assert.Equal(b, result[offset..(b.Length + offset)]);
                offset += b.Length;
            }
            
            Assert.Equal(buffer, result);
        }
        
        [Fact]
        public void Reset_NextReadOrWrite_ShouldNotExposePreviousData()
        {
            var stream = new MemorySegmentStream(ArrayPool<byte>.Shared, 16);
            var buffer = new byte[128];

            var rnd = new Random();
            rnd.NextBytes(buffer);
            stream.Write(buffer);
            
            stream.Reset();

            Assert.Equal(0, stream.Length);
            
            var result = new byte[16];
            var read = stream.Read(result);
            Assert.Equal(0, read);

            buffer = new byte[256];
            rnd.NextBytes(buffer);
            stream.Write(buffer);

            result = new byte[buffer.Length];
            read = stream.Read(result);
            Assert.Equal(buffer.Length, read);
            Assert.Equal(result, buffer);
        }
    }
}