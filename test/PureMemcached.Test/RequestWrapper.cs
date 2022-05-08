using System;
using System.IO;

namespace PureMemcached.Test;

internal struct RequestWrapper
{
    public uint Opaque { get; set; }

    public ulong Cas { get; set; }

    public OpCode OpCode { get; set; }

    public ReadOnlyMemory<byte> Key { get; set; }

    public ReadOnlyMemory<byte> Extra { get; set; }

    public Stream Value { get; set; }
            
    public static implicit operator Request(RequestWrapper w)
    {
        return new Request
        {
            Opaque = w.Opaque,
            Cas = w.Cas,
            OpCode = w.OpCode,
            Key = w.Key.Span,
            Extra = w.Extra.Span,
            Payload = w.Value
        };
    } 
}