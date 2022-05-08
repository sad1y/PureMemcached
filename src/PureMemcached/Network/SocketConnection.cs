using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached.Network;

public class SocketConnection : Connection, IEquatable<SocketConnection>
{
    private static readonly byte[] Buffer = new byte[4096];

    private readonly EndPoint _endPoint;
    private readonly Socket _socket;
    private int _status = Ready;

    private const int Ready = 0;
    private const int InProgress = 1;

    public SocketConnection(EndPoint endPoint, int responseBufferSize, int sendBufferSize, IObjectPool<Connection>? owner = null) :
        base(owner)
    {
        _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _socket.Blocking = false;
        _socket.ReceiveBufferSize = responseBufferSize;
        _socket.SendBufferSize = sendBufferSize;
        _endPoint = endPoint;
    }

    /// <summary>
    /// Send request data. exclusively 
    /// </summary>
    /// <param name="request"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public override async Task<Stream> SendAsync(Stream request, CancellationToken token)
    {
        // call of this method must be exclusive in another way you may see garbage in response
        if (Interlocked.CompareExchange(ref _status, InProgress, Ready) != Ready)
            throw new MemcachedClientException("You cannot send any data while the previous operation is not complete");

        var read = 0;
        var toSend = 0;
        do
        {
            read = await request.ReadAsync(Buffer.AsMemory(toSend), token).ConfigureAwait(false);
            toSend += read;

            // wait until buffer is full 
            if (toSend == Buffer.Length)
            {
                await SendBufferAsync(toSend, token).ConfigureAwait(false);
                toSend = 0;
            }
        } while (read > 0);

        // send if something is left
        if (toSend > 0)
            await SendBufferAsync(toSend, token).ConfigureAwait(false);

        return new ReadOnlySocketStream(_socket, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask SendBufferAsync(int size, CancellationToken token)
    {
        var sent = size;
        do
        {
            sent -= await _socket.SendAsync(Buffer.AsMemory(size - sent, sent), SocketFlags.None, token).ConfigureAwait(false);
        } while (sent != 0);
    }

    public bool Equals(SocketConnection? other)
    {
        if (ReferenceEquals(null, other)) return false;
        return ReferenceEquals(this, other) || _socket.Equals(other._socket);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj.GetType() == GetType() && Equals((SocketConnection)obj);
    }

    public override int GetHashCode()
    {
        return _socket.GetHashCode();
    }

    public static bool operator ==(SocketConnection? left, SocketConnection? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(SocketConnection? left, SocketConnection? right)
    {
        return !Equals(left, right);
    }

    public Task Connect() => _socket.Connected ? Task.CompletedTask : _socket.ConnectAsync(_endPoint);

    public override bool IsReady => _status == Ready && _socket.Connected;

    protected override bool TryRelease()
    {
        if (Interlocked.CompareExchange(ref _status, Ready, InProgress) != InProgress)
            throw new MemcachedClientException("Connection is in inconsistent state");

        return true;
    }

    internal void Close()
    {
        _socket.Close(1000);
        _socket.Dispose();
    }
}