using System;

namespace PureMemcached;

public abstract class Connection : IDisposable
{
    public abstract void BeginSend(
        byte[] buffer,
        int offset,
        int size,
        AsyncCallback callback,
        object state);

    public abstract void BeginReceive(
        byte[] buffer,
        int offset,
        int size,
        AsyncCallback callback,
        object state);
    
    public abstract int Complete(IAsyncResult result);
    
    public abstract bool IsHealthy { get; } 

    protected abstract void Dispose(bool disposing);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}


