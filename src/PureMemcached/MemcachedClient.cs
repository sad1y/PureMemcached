using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached
{
    public class MemcachedClient : IAsyncDisposable
    {
        private readonly Socket _socket;

        private readonly int _responseBufferSize;

        // private static readonly RecyclableMemoryStreamManager MemoryManager = new();
        private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Create(4096, 16);

        public MemcachedClient(string host,
            int port = 11211,
            int responseBufferSize = 4096,
            int sendBufferSize = 1024,
            TimeSpan sendTimeout = default,
            TimeSpan receiveTimeout = default
        )
        {
            _responseBufferSize = responseBufferSize;

            var ipHostInfo = Dns.GetHostEntry(host);
            var ipAddress = ipHostInfo.AddressList[0];
            var endpoint = new IPEndPoint(ipAddress, port);

            _socket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _socket.Blocking = false;
            _socket.ReceiveBufferSize = responseBufferSize;
            _socket.SendBufferSize = sendBufferSize;
            _socket.ReceiveTimeout = sendTimeout == TimeSpan.Zero ? 1000 : (int)receiveTimeout.TotalMilliseconds;
            _socket.SendTimeout = sendTimeout == TimeSpan.Zero ? 1000 : (int)sendTimeout.TotalMilliseconds;
            _socket.ConnectAsync(endpoint).Wait();
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

        private Task<Response> SendCommand(ref Request request, CancellationToken token)
        {
            var pooledStream = new MemorySegmentStream(ArrayPool);
            var writer = new BinaryProtocolWriter(pooledStream);
            writer.Write(ref request);

            var ts = new TaskCompletionSource<Response>();
            StartSend(new SendState(_socket, pooledStream, ts, token));

            return ts.Task;
        }

        private static void StartSend(SendState state)
        {
            try
            {
                if (state.Token.IsCancellationRequested)
                    throw new TaskCanceledException();

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

                    state.Socket.BeginSend(
                        segment.Array!, state.Offset, len,
                        SocketFlags.None,
                        out var error,
                        CompleteSend, state);

                    if (error is not (SocketError.Success or SocketError.IOPending))
                        throw new SocketException((int)error);
                }
                else
                    throw new IOException("failed to get stream data");
            }
            catch (Exception ex)
            {
                state.Stream.Dispose();
                state.Task.SetException(ex);
            }
        }

        private static void CompleteSend(IAsyncResult asyncResult)
        {
            var state = (SendState)asyncResult.AsyncState;
            var sent = state.Socket.EndSend(asyncResult);

            state.Sent += sent;

            if (sent > 0 && !state.IsComplete)
            {
                state.Offset += sent;
                StartSend(state);
                return;
            }

            if (state.Token.IsCancellationRequested)
                throw new TaskCanceledException();

            var buffer = ArrayPool.Rent(4096);
            try
            {
                state.Stream.Reset();
                state.Socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None,
                    CompleteReceive,
                    new ReceiveState(state.Socket, buffer, state.Stream, state.Task, state.Token));
            }
            catch (Exception ex)
            {
                ArrayPool.Return(buffer);
                state.Stream.Dispose();
                state.Task.SetException(ex);
            }
        }

        private static void CompleteReceive(IAsyncResult asyncResult)
        {
            var state = (ReceiveState)asyncResult.AsyncState;

            try
            {
                var read = state.Socket.EndSend(asyncResult);

                if (state.Token.IsCancellationRequested)
                    throw new TaskCanceledException();

                state.Stream.Write(state.Buffer, 0, read);

                if (read > 0)
                {
                    unsafe
                    {
                        if (state.Response == null)
                        {
                            if (read < sizeof(ResponseHeader))
                            {
                                state.Socket.BeginReceive(state.Buffer, 0, state.Buffer.Length, SocketFlags.None, CompleteReceive, state);
                                return;
                            }

                            var reader = new BinaryProtocolReader(state.Stream);
                            state.Response = reader.Read();
                        }

                        if (state.Response.TotalSize > state.Stream.Length)
                        {
                            state.Socket.BeginReceive(state.Buffer, 0, state.Buffer.Length, SocketFlags.None, CompleteReceive, state);
                            return;
                        }
                    }
                }

                if (state.Response != null)
                {
                    state.Task.SetResult(state.Response);
                }
                else
                    state.Task.SetException(new IOException("cannot read response"));
                
                ArrayPool.Return(state.Buffer);
            }
            catch (Exception ex)
            {
                ArrayPool.Return(state.Buffer);
                state.Stream.Dispose();
                state.Task.SetException(ex);
            }
        }

        public ValueTask DisposeAsync()
        {
            _socket.Disconnect(true);
            _socket.Dispose();
            return new ValueTask();
        }

        private class ReceiveState
        {
            public readonly Socket Socket;
            public readonly byte[] Buffer;
            public readonly MemorySegmentStream Stream;
            public readonly TaskCompletionSource<Response> Task;
            public readonly CancellationToken Token;
            public Response? Response;

            public ReceiveState(Socket socket, byte[] buffer, MemorySegmentStream segmentStream, TaskCompletionSource<Response> task,
                CancellationToken token)
            {
                Socket = socket;
                Buffer = buffer;
                Stream = segmentStream;
                Task = task;
                Token = token;
            }
        }

        private class SendState
        {
            public readonly Socket Socket;
            public readonly MemorySegmentStream Stream;
            public readonly TaskCompletionSource<Response> Task;
            public readonly CancellationToken Token;
            public int Block;
            public int Offset;
            public int Sent;

            public bool IsComplete => Stream.Length == Sent;

            public SendState(Socket socket, MemorySegmentStream segmentStream, TaskCompletionSource<Response> task, CancellationToken token)
            {
                Socket = socket;
                Stream = segmentStream;
                Task = task;
                Token = token;
            }
        }
    }
}