using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;

namespace PureMemcached
{
    public class MemorySegmentStream : Stream
    {
        private readonly ArrayPool<byte> _allocator;
        private int _segmentsCount;
        private BufferSegment[] _segments;

        private SegmentPosition _position;
        private int _length;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;

        public int SegmentCount => _segmentsCount;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }


        public MemorySegmentStream(ArrayPool<byte> allocator, int initialSize = 4096)
        {
            _allocator = allocator;
            _segments = new BufferSegment[2];
            _segments[0] = new BufferSegment(allocator.Rent(initialSize));
            _segmentsCount = 1;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ref var segment = ref _segments[_position.Block];
            var read = 0;

            while (count > 0)
            {
                var leftInSegment = segment.Offset - _position.Offset;

                if (leftInSegment == 0)
                {
                    if (_position.Block + 1 == _segmentsCount)
                        return read;
                    _position = new SegmentPosition { Block = _position.Block + 1 };
                    segment = ref _segments[_position.Block];
                    leftInSegment = segment.Offset - _position.Offset;
                }

                var len = Math.Min(leftInSegment, count);

                Buffer.BlockCopy(segment.Data, _position.Offset, buffer, offset, len);

                read += len;
                offset += len;
                count -= len;
                _position.Offset += len;
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            const int writeThreshold = 8;
            ref var currentSegment = ref _segments[_segmentsCount - 1];

            while (count > 0)
            {
                // if no space left in current buffer or its not worth to spend time to copy small chunk
                var spaceLeft = currentSegment.SpaceLeft;
                if (spaceLeft < writeThreshold && count > writeThreshold)
                {
                    if (_segments.Length == _segmentsCount)
                        Array.Resize(ref _segments, (int)(_segments.Length * 1.78));

                    var previousBlockSize = currentSegment.Data.Length;
                    currentSegment = ref _segments[_segmentsCount];

                    if (currentSegment.Data == null)
                    {
                        var nextBufferSize = Math.Max(count, previousBlockSize);
                        _segments[_segmentsCount] = new BufferSegment(_allocator.Rent(nextBufferSize));
                    }
                    currentSegment = ref _segments[_segmentsCount];
                    _segmentsCount++;
                }

                var len = currentSegment.Write(buffer, offset, count);
                _length += len;
                offset += len;
                count -= len;
            }
        }

        protected override void Dispose(bool disposing)
        {
            for (var i = 0; i < _segmentsCount; i++)
                _allocator.Return(_segments[i].Data);

            base.Dispose(disposing);
        }

        public bool TryGetBuffer(out ArraySegment<byte> block, int blockIndex)
        {
            if (blockIndex > _segmentsCount)
            {
                block = ArraySegment<byte>.Empty;
                return false;
            }

            block = new ArraySegment<byte>(_segments[blockIndex].Data, 0, _segments[blockIndex].Offset);

            return true;
        }

        public void Reset()
        {
            for (var i = 0; i < _segmentsCount; i++)
            {
                ref var seg = ref _segments[i];
                seg.Offset = 0;
            }
            
            _length = 0;
            _position = new SegmentPosition();
            _segmentsCount = 1;
        }

        private struct BufferSegment
        {
            public readonly byte[] Data;

            public int Offset;

            public BufferSegment(byte[] data)
            {
                Data = data;
                Offset = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Write(byte[] src, int offset, int count)
            {
                var len = Math.Min(SpaceLeft, count);
                Buffer.BlockCopy(src, offset, Data, Offset, len);
                Offset += len;

                return len;
            }

            public int SpaceLeft => Data.Length - Offset;
        }

        private struct SegmentPosition
        {
            public int Block;
            public int Offset;
        }
    }
}