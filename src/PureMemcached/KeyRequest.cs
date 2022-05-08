using System;

namespace PureMemcached;

public class KeyRequest
{
    /// <summary>
    /// Key value. Length must be less or equal to 250 
    /// </summary>
    public ReadOnlyMemory<byte> Key { get; }
    
    /// <summary>
    /// Value that will be copies back with response
    /// </summary>
    public uint Opaque { get; }

    public KeyRequest(ReadOnlyMemory<byte> key, uint opaque)
    {
        Key = key;
        Opaque = opaque;
    }
}