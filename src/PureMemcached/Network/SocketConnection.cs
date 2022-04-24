using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached.Network;

public class SocketConnection : Connection, IEquatable<SocketConnection>
{
    private const int MaxBufferSize = 1024;
    private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Create(MaxBufferSize, 100);

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
    public override Task<Stream> SendAsync(Stream request, CancellationToken token)
    {
        // we need to protect the current send session, in other way we may see garbage in response
        if (Interlocked.CompareExchange(ref _status, InProgress, Ready) != Ready)
            throw new MemcachedClientException("You cannot send any data while the previous operation is not complete");

        async Task<Stream> TransmitData(Stream stream, CancellationToken cancellationToken)
        {
            var buffer = BufferPool.Rent((int)Math.Min(stream.Length, MaxBufferSize));
            var sentTotal = 0;
            try
            {
                while (sentTotal != stream.Length)
                {
                    var sent = 0;
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    do
                    {
                        sent += await _socket.SendAsync(buffer.AsMemory(0, read), SocketFlags.None, cancellationToken);
                    } while (read > sent);

                    sentTotal += sent;
                }

                return new ReadOnlySocketStream(_socket, this);
            }
            finally
            {
                BufferPool.Return(buffer);
            }
        }

        return TransmitData(request, token);
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