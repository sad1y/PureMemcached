using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace PureMemcached.Network;

public class SocketConnection : Connection
{
    private readonly EndPoint _endPoint;
    private readonly Socket _socket;

    public SocketConnection(EndPoint endPoint, int responseBufferSize, int sendBufferSize,
        int sendTimeoutMs,
        int receiveTimeoutMs)
    {
        _endPoint = endPoint;
        _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _socket.Blocking = false;
        _socket.ReceiveBufferSize = responseBufferSize;
        _socket.SendBufferSize = sendBufferSize;
        _socket.ReceiveTimeout = receiveTimeoutMs;
        _socket.SendTimeout = sendTimeoutMs;
    }

    public Task Connect() => _socket.Connected ? Task.CompletedTask : _socket.ConnectAsync(_endPoint);

    public override void BeginSend(byte[] buffer, int offset, int size, AsyncCallback? callback, object? state)
    {
        _socket.BeginSend(buffer, offset, size, SocketFlags.None, out var error, callback, state);
        EnsureSuccessCode(error);
    }

    public override void BeginReceive(byte[] buffer, int offset, int size, AsyncCallback? callback, object? state)
    {
        _socket.BeginReceive(buffer, offset, size, SocketFlags.None, out var error, callback, state);
        EnsureSuccessCode(error);
    }

    public override int Complete(IAsyncResult result)
    {
        var read = _socket.EndSend(result, out var error);
        EnsureSuccessCode(error);
        return read;
    }

    public override bool IsHealthy => _socket.Connected;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureSuccessCode(SocketError error)
    {
        if (error is not (SocketError.Success or SocketError.IOPending))
            throw new SocketException((int)error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _socket.Close(1000);
            _socket.Dispose();
        }
    }
}