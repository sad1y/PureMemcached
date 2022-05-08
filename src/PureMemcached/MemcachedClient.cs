using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PureMemcached.Network;

namespace PureMemcached
{
    /// <summary>
    /// Core class that allows to communicate with a <c>memcached</c>
    /// </summary>
    public class MemcachedClient : IAsyncDisposable
    {
        private readonly IObjectPool<Connection> _connectionPool;
        private volatile bool _disposed;

        /// <summary>
        /// should be used for tests only
        /// </summary>
        /// <param name="connectionPool"></param>
        internal MemcachedClient(IObjectPool<Connection> connectionPool)
        {
            Debug.Assert(connectionPool != null, nameof(connectionPool) + " != null");
            _connectionPool = connectionPool;
        }

        /// <summary>
        /// Create a socket based connection to a <c>memcached</c> 
        /// </summary>
        /// <param name="host">memcached address</param>
        /// <param name="port">memcached port</param>
        /// <param name="sendBufferSize">socket buffer size to send</param>
        /// <param name="receiveBufferSize">socket buffer size to receive</param>
        /// <param name="refreshDns">DNS refresh rate. 5 minutes by default. If it equals to <c>TimeSpan.Zero</c> updates will not be performed</param>
        public MemcachedClient(string host, int port = 11211, int sendBufferSize = 1024, int receiveBufferSize = 1024,
            TimeSpan? refreshDns = null)
        {
            _connectionPool = new SocketConnectionPool(host, port,
                sendBufferSize,
                receiveBufferSize,
                refreshDns ?? TimeSpan.FromMinutes(5)
            );
        }

        /// <summary>
        /// Perform several GetQ requests (initial requests) as one request to reduce network roundtrips.
        /// This one request consists of some keys and some opaque tokens.
        /// The keys will be checked on the server.
        /// Responses will come only for keys that existed on the server.
        /// Keys will not be returned in the responses.
        /// To order responses use opaque tokens.
        /// </summary>
        /// <param name="requests">List of <see cref="T:PureMemcached.KeyRequest" /> which will be sent to the server.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns><c>MultiResponse</c> that must be enumerated asynchronously. You must treat every item from that iterator as an ephemeral object and must not be stored anywhere for later use.</returns>
        /// <example>
        /// This shows how it may be used
        /// <code>
        /// var requests = new KeyRequest[] { new(key1, 0), new(key2, 1) };  
        /// var responses = await client.Get(requests, cancellationToken);
        /// await foreach (var response in responses) {
        ///     // the opaque token matches the response to the key
        ///     var key = requests[response.Opaque].Key;
        ///
        ///     // after you get a body stream you can consume it by copying it or any other way you like
        ///     var body = response.GetBody();
        ///     await body.CopyToAsync(otherStream, cancellationToken);
        ///     ...
        /// }
        /// </code>
        /// </example>
        public Task<MultiResponse> Get(IEnumerable<KeyRequest> requests, CancellationToken token)
        {
            if (requests == null)
                return Task.FromResult(new MultiResponse(Stream.Null));

            var requestStream = new MultiGetRequestStream(requests);
            return ExecuteMultiOperation(requestStream, token);
        }

        /// <summary>
        /// Perform several GetQ requests (initial requests) as one request to reduce network roundtrips.
        /// This one request consists of some keys and some opaque tokens.
        /// The keys will be checked on the server.
        /// Responses will come only for keys that existed on the server.
        /// Keys will not be returned in the responses.
        /// To order responses use opaque tokens.
        /// </summary>
        /// <param name="keys">List of keys which will be sent to the server.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>Iterator over <see cref="T:PureMemcached.KeyData" />. You must consume data before taking next element in another case it will be lost</returns>
        /// <example>
        /// This shows how it may be used
        /// <code>
        /// var responses = client.Get(new[] { key1, key2 }, CancellationToken.None);
        /// await foreach (var keyData in responses)
        /// {
        ///    Console.WriteLine("{0}, {1}", keyData.Key, await keyData.Body.ReadAsUtf8StringAsync(CancellationToken.None));
        /// }
        /// </code>
        /// </example>
        public async IAsyncEnumerable<KeyData> Get(IEnumerable<byte[]> keys, [EnumeratorCancellation] CancellationToken token)
        {
            var requests = new List<KeyRequest>();

            uint counter = 0;

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var key in keys)
            {
                if (key == null || key.Length == 0)
                    continue;
                requests.Add(new KeyRequest(key, counter++));
            }

            await using var responses = await Get(requests, token).ConfigureAwait(false);

            await foreach (var response in responses.WithCancellation(token).ConfigureAwait(false))
            {
                var request = requests[(int)response.Opaque];
                yield return new KeyData(request.Key, response.GetBody());
            }
        }

        /// <summary>
        /// Perform a single request to get data by a key.
        /// </summary>
        /// <param name="key">Key value. Length must be less or equal to 250.</param>
        /// <param name="opaque">Value that will be copied back with response.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// Response will contain data associated with the requested key.
        /// If method <c>HasError</c> returns <c>false</c> you may treat the stream from <c>GetBody</c> as a payload, in another case it will contain an error message.
        /// You must not store or reuse neither a response object nor a stream from <c>GetBody</c> method, it must be disposed as soon as possible. 
        /// </returns>
        /// <example>
        /// This shows how it may be used
        /// <code>
        /// var key = new byte [] {...};  
        /// await using var response = await client.Get(key, token: cancellationToken);
        /// await using var body = response.GetBody();
        /// 
        /// if (response.HasError()) {
        ///     var bodyMsg = ...
        ///     throw new Exception(bodyMsg); 
        /// }
        ///
        /// // read body here
        /// 
        /// </code>
        /// </example>
        public Task<Response> Get(in ReadOnlySpan<byte> key, uint opaque = 0, CancellationToken token = default)
        {
            var request = new Request
            {
                OpCode = OpCode.Get,
                Opaque = opaque,
                Key = key
            };

            return ExecuteSingleOperation(ref request, token);
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _connectionPool.Dispose();
                GC.SuppressFinalize(this);
            }

            return new ValueTask();
        }

        private async Task<MultiResponse> ExecuteMultiOperation(Stream request, CancellationToken token)
        {
            var response = await SendInternalAsync(request, token).ConfigureAwait(false);
            return new MultiResponse(response);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Task<Response> ExecuteSingleOperation(ref Request request, CancellationToken token)
        {
            var stream = new RequestStream(ref request);
            return Core(stream);

            async Task<Response> Core(Stream requestStream)
            {
                var response = await SendInternalAsync(requestStream, token).ConfigureAwait(false);
                return await Response.FromStream(response, token).ConfigureAwait(false);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task<Stream> SendInternalAsync(Stream request, CancellationToken token)
        {
            var connection = await _connectionPool.RentAsync().ConfigureAwait(false);
            try
            {
                return await connection.SendAsync(request, token).ConfigureAwait(false);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                await request.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}