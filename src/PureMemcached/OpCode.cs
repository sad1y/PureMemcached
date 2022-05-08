namespace PureMemcached
{
    public enum OpCode : byte
    {
        Get = 0,
        Quit = 0x07,
        GetQ = 0x09,
        NoOp = 0x0a,
        GetK = 0x0c,
        GetKQ = 0x0d,
    }
}