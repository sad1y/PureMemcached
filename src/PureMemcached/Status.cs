namespace PureMemcached
{
    public enum Status : ushort
    {
        NoError = 0,
        KeyNotFound = 1,
        KeyExists = 2,
        ValueTooLarge = 3,
        InvalidArguments = 4,
        ItemNotStored = 5,
        IncrDecrOnNonNumericValue = 6,
        UnknownCommand = 81,
        OutOfMemory = 82,
    }
}