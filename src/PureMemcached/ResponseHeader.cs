namespace PureMemcached
{
    public readonly struct ResponseHeader
    {
        public readonly byte Magic;
        public readonly OpCode OpCode;
        public readonly ushort KeyLength;
        public readonly byte ExtraLength;
        public readonly byte DataType;
        public readonly Status Status;
        public readonly uint TotalSize;
        public readonly uint Opaque;
        public readonly ulong Cas;

        public ResponseHeader(byte magic, OpCode opCode, ushort keyLength, byte extraLength, byte dataType, Status status,
            uint totalSize, uint opaque, ulong cas)
        {
            Magic = magic;
            OpCode = opCode;
            KeyLength = keyLength;
            ExtraLength = extraLength;
            DataType = dataType;
            Status = status;
            TotalSize = totalSize;
            Opaque = opaque;
            Cas = cas;
        }
    }
}