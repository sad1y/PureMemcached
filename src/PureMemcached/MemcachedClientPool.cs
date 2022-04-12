using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;

namespace PureMemcached;

public class MemcachedClientPool : IObjectPool<MemcachedClient>, IDisposable
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly ConcurrentQueue<MemcachedClient> _freeClients;
    private readonly IncrementingEventCounter _stolenClientCounter;
    private readonly IncrementingEventCounter _notReadyClientCounter;
    private volatile bool _disposed;

    private const string CounterName = nameof(PureMemcached) + "-" + nameof(MemcachedClientPool);

    public MemcachedClientPool(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _freeClients = new ConcurrentQueue<MemcachedClient>();
        _stolenClientCounter = new IncrementingEventCounter(CounterName, new EventSource("StolenClientCount"));
        _notReadyClientCounter = new IncrementingEventCounter(CounterName, new EventSource("NotReadyClientReturnCount"));
    }

    public ValueTask<MemcachedClient> RentAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MemcachedClientPool));

        while (!_freeClients.IsEmpty)
        {
            if (!_freeClients.TryDequeue(out var client)) continue;

            if (client.IsReady())
                return new ValueTask<MemcachedClient>(client);

            // that should be rare case
            // someone return client before done with it and probably use to make requests 
            // we can't put that client back in list, it will lead grow queue and makes thing even worst
            // count it and release client
            _stolenClientCounter.Increment();
            client.Release();
        }

        async ValueTask<MemcachedClient> CreateNewInternalAsync() => new(await _connectionFactory.CreateAsync(), parent: this);

        return CreateNewInternalAsync();
    }

    public ValueTask ReturnAsync(MemcachedClient client)
    {
        if (!client.IsReady())
        {
            _notReadyClientCounter.Increment();
            return new ValueTask();
        }

        _freeClients.Enqueue(client);
        return new ValueTask();
    }

    public void Dispose()
    {
        _disposed = true;

        if (_disposed)
            return;

        while (!_freeClients.IsEmpty)
        {
            if (_freeClients.TryDequeue(out var client))
            {
                client.Release();
            }
        }

        GC.SuppressFinalize(this);
    }
}