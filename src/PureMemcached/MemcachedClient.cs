using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PureMemcached.Protocol;

namespace PureMemcached
{
    public class MemcachedClient : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly ObjectPool<MemcachedClient>? _parent;
        private readonly ArrayPool<byte> _allocator;

        public MemcachedClient(Connection connection, int blockSize = 4096, ObjectPool<MemcachedClient>? parent = null)
        {
            
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _allocator = ArrayPool<byte>.Create(blockSize, 16);
            _parent = parent;
        }

        public bool IsConnected() => _connection.IsHealthy;

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
            if (_parent != null)
                return _parent.ReturnAsync(this);

            Release();

            return new ValueTask();
        }

        internal void Release() => _connection.Dispose();

        private Task<Response> SendCommand(ref Request request, CancellationToken token)
        {
            var pooledStream = new MemorySegmentStream(_allocator);
            var writer = new BinaryProtocolWriter(pooledStream);
            writer.Write(ref request);

            var ts = new TaskCompletionSource<Response>();
            StartSend(new SendState(_connection, pooledStream, _allocator, ts, token));

            return ts.Task;
        }

        private static void StartSend(SendState state)
        {
            try
            {
                if (state.Token.IsCancellationRequested)
                {
                    state.MarkAsFailed(new TaskCanceledException());
                    return;
                }

                if (state.Stream.TryGetBuffer(out var segment, state.Block))
                {
                    var len = segment.Count - state.Offset;
                    if (len == 0)
                    {
                        state.Block++;
                        state.Offset = 0;

                        StartSend(state);
                        return;
                    }

                    state.Connection.BeginSend(
                        segment.Array!, state.Offset, len, CompleteSend, state);
                }
                else
                    throw new MemcachedClientException("failed to get stream data");
            }
            catch (Exception ex)
            {
                state.MarkAsFailed(new MemcachedClientException("cannot send request", ex));
            }
        }

        private static void CompleteSend(IAsyncResult asyncResult)
        {
            var state = (SendState)asyncResult.AsyncState;
            var sent = state.Connection.Complete(asyncResult);

            state.Sent += sent;

            if (state.Token.IsCancellationRequested)
            {
                state.MarkAsFailed(new TaskCanceledException());
                return;
            }

            if (sent > 0 && !state.IsComplete)
            {
                state.Offset += sent;
                StartSend(state);
                return;
            }

            var buffer = state.Allocator.Rent(4096);
            try
            {
                state.Stream.Reset();
                state.Connection.BeginReceive(buffer, 0, buffer.Length,
                    CompleteReceive,
                    new ReceiveState(state.Connection, buffer, state.Stream, state.Allocator, state.Task, state.Token));
            }
            catch (Exception ex)
            {
                state.Allocator.Return(buffer);
                state.MarkAsFailed(new MemcachedClientException("cannot complete send", ex) );
            }
        }

        private static void CompleteReceive(IAsyncResult asyncResult)
        {
            var state = (ReceiveState)asyncResult.AsyncState;

            try
            {
                var read = state.Socket.Complete(asyncResult);

                if (state.Token.IsCancellationRequested)
                    throw new TaskCanceledException();

                state.Stream.Write(state.Buffer, 0, read);

                if (read > 0)
                {
                    unsafe
                    {
                        if (state.Response == null)
                        {
                            if (state.Stream.Length < sizeof(ResponseHeader))
                            {
                                state.Socket.BeginReceive(state.Buffer, 0, state.Buffer.Length, CompleteReceive, state);
                                return;
                            }

                            var reader = new BinaryProtocolReader(state.Stream);
                            state.Response = reader.Read();
                        }

                        if (state.Response.TotalSize > state.Stream.Length - sizeof(ResponseHeader))
                        {
                            state.Socket.BeginReceive(state.Buffer, 0, state.Buffer.Length, CompleteReceive, state);
                            return;
                        }
                    }
                }

                state.Allocator.Return(state.Buffer);

                if (state.Response != null)
                    state.Task.SetResult(state.Response);
                else
                    state.Task.SetException(new MemcachedClientException("cannot read response"));
            }
            catch (Exception ex)
            {
                state.Allocator.Return(state.Buffer);
                state.Stream.Dispose();
                state.Task.SetException(new MemcachedClientException("cannot complete receive", ex));
            }
        }

        internal class ReceiveState
        {
            internal readonly Connection Socket;
            internal readonly byte[] Buffer;
            internal readonly MemorySegmentStream Stream;
            internal readonly TaskCompletionSource<Response> Task;
            internal readonly CancellationToken Token;
            internal readonly ArrayPool<byte> Allocator;
            internal Response? Response;

            public ReceiveState(Connection socket, byte[] buffer, MemorySegmentStream stream, ArrayPool<byte> allocator,
                TaskCompletionSource<Response> task,
                CancellationToken token)
            {
                Socket = socket;
                Buffer = buffer;
                Allocator = allocator;
                Stream = stream;
                Task = task;
                Token = token;
            }
        }

        internal class SendState
        {
            internal readonly Connection Connection;
            internal readonly MemorySegmentStream Stream;
            internal readonly TaskCompletionSource<Response> Task;
            internal readonly CancellationToken Token;
            internal readonly ArrayPool<byte> Allocator;
            internal int Block;
            internal int Offset;
            internal int Sent;

            public bool IsComplete => Stream.Length == Sent;

            public SendState(Connection connection,
                MemorySegmentStream stream,
                ArrayPool<byte> allocator,
                TaskCompletionSource<Response> task,
                CancellationToken token)
            {
                Connection = connection;
                Stream = stream;
                Task = task;
                Token = token;
                Allocator = allocator;
            }

            public void MarkAsFailed(Exception exception)
            {
                Stream.Dispose();
                Task.SetException(exception);
            }
        }
    }
}