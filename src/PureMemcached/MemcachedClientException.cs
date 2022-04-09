using System;

namespace PureMemcached;

public class MemcachedClientException : Exception
{
    public MemcachedClientException(string text) : base(text)
    {
        
    }
    
    public MemcachedClientException(string text, Exception innerException) : base(text, innerException)
    {
        
    }
}