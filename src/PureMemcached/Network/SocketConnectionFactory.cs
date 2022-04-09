using System;
using System.Net;
using System.Threading.Tasks;

namespace PureMemcached.Network;

public class SocketConnectionFactory : ConnectionFactory<SocketConnection>
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _sendBufferSize;
    private readonly int _responseBufferSize;
    private readonly int _sendTimeoutMs;
    private readonly int _receiveTimeoutMs;
    private readonly int _dnsRefreshTimeout;
    private readonly int _lastDnsUpdate;
    
    private IPEndPoint? _endpoint;

    public SocketConnectionFactory(
        string host,
        int port,
        int sendBufferSize,
        int responseBufferSize,
        int sendTimeoutMs,
        int receiveTimeoutMs,
        TimeSpan dnsRefreshTimeout)
    {
        _host = host;
        _port = port;
        _sendBufferSize = sendBufferSize;
        _responseBufferSize = responseBufferSize;
        _sendTimeoutMs = sendTimeoutMs;
        _receiveTimeoutMs = receiveTimeoutMs;
        _dnsRefreshTimeout = (int)dnsRefreshTimeout.TotalMilliseconds;
        _lastDnsUpdate = 0;
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

    public override async Task<SocketConnection> CreateAsync()
    {
        var ipAddress = await GetEndpointAsync();
        var connection = new SocketConnection(ipAddress, _responseBufferSize, _sendBufferSize, _sendTimeoutMs, _receiveTimeoutMs);
        await connection.Connect();
        return connection;
    }

    public void Dispose()
    {
    }
}