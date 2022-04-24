using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PureMemcached.Network;

namespace PureMemcached.Test.FunctionalTests;

public abstract class ConnectionTestBase
{
    protected static async Task StartEnv(IEnumerable<ServerMock.SendReceiveMock> sendReceiveMocks, Func<SocketConnectionPool, Task> clientRun)
    {
        const int port = 22222;
        using var server = new ServerMock(port);
        server.Start(sendReceiveMocks);

        using var pool = new SocketConnectionPool("localhost", port, 1024, 1024, TimeSpan.FromMinutes(5));
        await clientRun(pool).ConfigureAwait(false);
    }
}