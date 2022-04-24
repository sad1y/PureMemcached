using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached;

public abstract class Connection : IAsyncDisposable
{
    private readonly IObjectPool<Connection>? _owner;

    protected Connection(IObjectPool<Connection>? owner)
    {
        _owner = owner;
    }

    public abstract Task<Stream> SendAsync(Stream request, CancellationToken token);

    public abstract bool IsReady { get; }

    protected abstract bool TryRelease();

    public ValueTask DisposeAsync()
    {
        if (TryRelease() && _owner != null)
            return _owner.ReturnAsync(this);
        
        return new ValueTask();
    }
}