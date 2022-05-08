using System;
using System.IO;

namespace PureMemcached
{
    internal ref struct Request
    {
        /// <summary>
        /// Will be copied back to you in the response. Default zero
        /// </summary>
        public uint Opaque { get; set; }

        /// <summary>
        /// Data version check. Default zero
        /// </summary>
        public ulong Cas { get; set; }

        /// <summary>
        /// Command code
        /// </summary>
        public OpCode OpCode { get; set; }

        /// <summary>
        /// Record key, optional, should be empty if not needed 
        /// </summary>
        public ReadOnlySpan<byte> Key { get; set; }

        /// <summary>
        /// Flags and ttl, optional, should be empty if not needed 
        /// </summary>
        public ReadOnlySpan<byte> Extra { get; set; }

        /// <summary>
        /// Payload, should be empty if not needed 
        /// </summary>
        public Stream? Payload { get; set; }
    }
}