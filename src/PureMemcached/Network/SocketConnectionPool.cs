using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Net;
using System.Threading.Tasks;

namespace PureMemcached.Network;

public class SocketConnectionPool : IObjectPool<Connection>
{
    private const string CounterName = nameof(PureMemcached) + "-" + nameof(SocketConnectionPool);

    private readonly ConcurrentQueue<SocketConnection> _freeConnections;
    private volatile bool _disposed;

    private readonly IncrementingEventCounter _stolenClientCounter;
    private readonly IncrementingEventCounter _notReadyClientCounter;

    private readonly string _host;
    private readonly int _port;

    private readonly int _sendBufferSize;
    private readonly int _receiveBufferSize;

    private readonly int _dnsRefreshTimeout;
    private readonly int _lastDnsUpdate;

    private IPEndPoint? _endpoint;

    public SocketConnectionPool(string host,
        int port,
        int sendBufferSize,
        int receiveBufferSize,
        TimeSpan dnsRefreshTimeout)
    {
        _host = host;
        _port = port;
        _sendBufferSize = sendBufferSize;
        _receiveBufferSize = receiveBufferSize;
        _dnsRefreshTimeout = (int)dnsRefreshTimeout.TotalMilliseconds;
        _lastDnsUpdate = 0;

        _stolenClientCounter = new IncrementingEventCounter(CounterName, new EventSource("StolenConnectionCount"));
        _notReadyClientCounter = new IncrementingEventCounter(CounterName, new EventSource("ReturnNotReadyConnectionCount"));
        _freeConnections = new ConcurrentQueue<SocketConnection>();
    }

    public ValueTask<Connection> RentAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SocketConnectionPool));

        while (!_freeConnections.IsEmpty)
        {
            if (!_freeConnections.TryDequeue(out var connection)) continue;

            if (connection.IsReady)
                return new ValueTask<Connection>(connection);

            // that should be rare case
            // someone returned a client before the actual request had completed and probably used to make requests
            // or return connections multiple times.
            // we can't put that client back to the list, it will lead to a grow queue and make things even worse.
            // count it and release the client.
            _stolenClientCounter.Increment();
            connection.Close();
        }

        return CreateAsync();
    }

    public ValueTask ReturnAsync(Connection connection)
    {
        if (!connection.IsReady)
        {
            _notReadyClientCounter.Increment();
            return new ValueTask();
        }

        _freeConnections.Enqueue((SocketConnection)connection);
        return new ValueTask();
    }

    public void Dispose()
    {
        _disposed = true;

        if (_disposed)
            return;

        while (!_freeConnections.IsEmpty)
        {
            if (_freeConnections.TryDequeue(out var client))
            {
                client.Close();
            }
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask<Connection> CreateAsync()
    {
        var ipAddress = await GetEndpointAsync();
        var connection = new SocketConnection(ipAddress, _receiveBufferSize, _sendBufferSize, this);
        await connection.Connect();
        return connection;
    }

    private async ValueTask<IPEndPoint> GetEndpointAsync()
    {
        if (Environment.TickCount - _lastDnsUpdate > _dnsRefreshTimeout)
        {
            var ipHostInfo = await Dns.GetHostEntryAsync(_host);
            _endpoint = new IPEndPoint(ipHostInfo.AddressList[0], _port);
        }

        return _endpoint ?? throw new InvalidOperationException("cannot get ip endpoint");
    }
}