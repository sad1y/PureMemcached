using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PureMemcached.Test.FunctionalTests;

public class ServerMock : IDisposable
{
    private readonly byte[] _buffer;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _tsc;
    private Socket _channel;
    

    public ServerMock(int port = 12345, int bufferSize = 1024)
    {
        var serverAddress = IPAddress.Any;

        _buffer = new byte[bufferSize];
        _tsc = new CancellationTokenSource();
        _listener = new Socket(serverAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(serverAddress, port));
        _listener.Listen(1);
    }

    public void Start(IEnumerable<SendReceiveMock> sendReceiveMocks)
    {
        Task.Run(async () =>
        {
            _channel = await _listener.AcceptAsync(_tsc.Token).ConfigureAwait(false);
            _channel.Blocking = false;

            foreach (var sendReceive in sendReceiveMocks)
            {
                // don't care about request, we pretend that we know exact order of command
                await ReceiveCommand(sendReceive).ConfigureAwait(false);
                await SendAnswer(sendReceive).ConfigureAwait(false);
            }
        }, _tsc.Token);
    }

    private async Task SendAnswer(SendReceiveMock sendReceive)
    {
        if (sendReceive.Delay.TotalMilliseconds > 0)
            await Task.Delay(sendReceive.Delay);

        var sent = 0;
        do
        {
            sent += await _channel.SendAsync(sendReceive.Response.AsMemory(sent), SocketFlags.None, _tsc.Token).ConfigureAwait(false);
        } while (sent < sendReceive.Response.Length);
    }

    private async Task ReceiveCommand(SendReceiveMock sendReceive)
    {
        var read = 0;

        do
        {
            read += await _channel.ReceiveAsync(_buffer, SocketFlags.None, _tsc.Token).ConfigureAwait(false);
        } while (read < sendReceive.RequestSize);
    }

    public void Dispose()
    {
        _tsc.Cancel();
        _listener?.Dispose();
        _channel?.Dispose();
    }

    public class SendReceiveMock
    {
        public int RequestSize { get; }
        public byte[] Response { get; }
        public TimeSpan Delay { get; }

        public SendReceiveMock(byte[] response, int requestSize, TimeSpan delay = default)
        {
            Response = response;
            RequestSize = requestSize;
            Delay = delay;
        }
    }
}