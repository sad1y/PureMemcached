using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using PureMemcached.Protocol;

namespace PureMemcached
{
    public class MemcachedClient : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly IObjectPool<MemcachedClient>? _parent;
        private readonly ArrayPool<byte> _allocator;
        private volatile bool _disposed;
        private int _canHandleNextCommand = 1;

        private static readonly unsafe int ResponseHeaderSize = sizeof(ResponseHeader);

        public MemcachedClient(Connection connection, int blockSize = 4096, IObjectPool<MemcachedClient>? parent = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _allocator = ArrayPool<byte>.Create(blockSize, 16);
            _parent = parent;
        }

        public bool IsReady() => !_disposed && _canHandleNextCommand == 1 && _connection.IsReady;

        public Task<Response> Get(in ReadOnlySpan<byte> key, uint requestId = 0, ulong cas = 0, CancellationToken token = default)
        {
            ThrowIfKeyIsNotValid(key);
            
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

        private static void ThrowIfKeyIsNotValid(in ReadOnlySpan<byte> key)
        {
            var result = ValidateKey(key);
            if(result > 0)
                throw new KeyNotValidException(result);
        }
        
        private static KeyValidationResult ValidateKey(in ReadOnlySpan<byte> key)
        {
            if (key.IsEmpty)
                return KeyValidationResult.Empty;

            if (key.Length > 250)
                return KeyValidationResult.TooLong;
            
            return KeyValidationResult.OK;
        }
        
        internal void Release()
        {
            try
            {
                if (_disposed)
                    return;

                _disposed = true;
                _connection.Dispose();
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }

        private Task<Response> SendCommand(ref Request request, CancellationToken token)
        {
            if (Interlocked.CompareExchange(ref _canHandleNextCommand, 0, 1) == 0)
                throw new MemcachedClientException("Cannot send command. Previous operation has not completed yet");

            var pooledStream = new MemorySegmentStream(_allocator);
            var writer = new BinaryProtocolWriter(pooledStream);
            writer.Write(ref request);

            var ts = new TaskCompletionSource<Response>();
            StartSend(new SendState(_connection, pooledStream, _allocator, ts, token));

            return ts.Task.ContinueWith((task, state) =>
            {
                var client = (MemcachedClient)state;
                client.MakeItAvailable();
                return task.Result;
            }, this, token);
        }

        private void MakeItAvailable()
        {
            if (Interlocked.CompareExchange(ref _canHandleNextCommand, 1, 0) == 1)
                throw new MemcachedClientException("Cannot reset client state");
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
                state.MarkAsFailed(new MemcachedClientException("cannot complete send", ex));
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
                    if (state.Response == null)
                    {
                        if (state.Stream.Length < ResponseHeaderSize)
                        {
                            state.Socket.BeginReceive(state.Buffer, 0, state.Buffer.Length, CompleteReceive, state);
                            return;
                        }

                        var reader = new BinaryProtocolReader(state.Stream);
                        state.Response = reader.Read();
                    }

                    if (state.Response.TotalSize > state.Stream.Length - ResponseHeaderSize)
                    {
                        state.Socket.BeginReceive(state.Buffer, 0, state.Buffer.Length, CompleteReceive, state);
                        return;
                    }
                }

                state.Allocator.Return(state.Buffer);

                if (state.Response != null)
                    state.Task.SetResult(state.Response);
                else
                    state.Task.SetException(new MemcachedClientException("Response does not have header"));
            }
            catch (Exception ex)
            {
                state.Allocator.Return(state.Buffer);
                state.Stream.Dispose();
                state.Task.SetException(new MemcachedClientException("Cannot complete receive", ex));
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