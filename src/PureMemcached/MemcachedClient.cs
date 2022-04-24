using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PureMemcached.Extensions;
using PureMemcached.Network;

namespace PureMemcached
{
    public class MemcachedClient : IAsyncDisposable
    {
        private readonly IObjectPool<Connection> _connectionPool;
        private volatile bool _disposed;

        internal MemcachedClient(IObjectPool<Connection> connectionPool)
        {
            Debug.Assert(connectionPool != null, nameof(connectionPool) + " != null");
            _connectionPool = connectionPool;
        }

        public MemcachedClient(string host, int port = 11211, int sendBufferSize = 1024, int receiveBufferSize = 1024,
            TimeSpan? refreshDns = null)
        {
            _connectionPool = new SocketConnectionPool(host, port,
                sendBufferSize,
                receiveBufferSize,
                refreshDns ?? TimeSpan.FromMinutes(5)
            );
        }

        public Task<Response> Get(in ReadOnlySpan<byte> key, uint requestId = 0, ulong cas = 0, CancellationToken token = default)
        {
            var request = new Request
            {
                RequestId = requestId,
                Cas = cas,
                OpCode = OpCode.Get,
                Key = key
            };

            return SendCommand(ref request, token);
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                Release();
            }

            return new ValueTask();
        }

        private Task<Response> SendCommand(ref Request request, CancellationToken token)
        {
            var stream = new RequestStream(ref request);

            async Task<Response> SendAsync(Stream req, CancellationToken t)
            {
                var connection = await _connectionPool.RentAsync().ConfigureAwait(false);
                var buffer = ArrayPool<byte>.Shared.Rent(Protocol.HeaderSize);
                try
                {
                    var res = await connection.SendAsync(req, t).ConfigureAwait(false);
                    res.SetLength(Protocol.HeaderSize);
                    await res.ReadExactAsync(buffer.AsMemory(0, Protocol.HeaderSize), t).ConfigureAwait(false);
                    var header = Protocol.ReadHeader(buffer, out _);
                    res.SetLength(header.TotalSize + Protocol.HeaderSize);
                    return new Response(header, res);
                }
                catch
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            return SendAsync(stream, token);
        }

        private void Release()
        {
            if (_disposed)
                return;

            _connectionPool.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}