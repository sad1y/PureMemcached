using System;
using System.IO;

namespace PureMemcached;

public class KeyData : IDisposable
{
    public ReadOnlyMemory<byte> Key { get; }
    public Stream Body { get; }

    public KeyData(ReadOnlyMemory<byte> key, Stream body)
    {
        Key = key;
        Body = body;
    }

    public void Dispose()
    {
        Body.Dispose();
    }
}