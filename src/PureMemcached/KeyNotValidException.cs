using System;

namespace PureMemcached;

internal class KeyNotValidException : MemcachedClientException
{
    public KeyValidationResult Reason { get; }

    public KeyNotValidException(KeyValidationResult reason) : base("Key that your are trying to use is not valid")
    {
        Reason = reason;
    }
}