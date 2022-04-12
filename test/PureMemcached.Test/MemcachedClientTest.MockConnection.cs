using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using PureMemcached.Protocol;
using Xunit;

namespace PureMemcached.Test;

public partial class MemcachedClientTest
{
    /// <summary>
    /// facilities for client testing
    /// </summary>
    private class MockConnection : Connection
    {
        private readonly ExecutionFlow _flow;

        public MockConnection(ExecutionFlow flow)
        {
            _flow = flow;
        }

        public override void BeginSend(byte[] buffer, int offset, int size, AsyncCallback callback, object state)
        {
            _flow.Validate(new BeginSendState(buffer, offset, size));
            callback(CreateAsyncResultFromState(state));
        }

        public override void BeginReceive(byte[] buffer, int offset, int size, AsyncCallback callback, object state)
        {
            _flow.Validate(new BeginReceiveState(offset, size));
            callback(CreateAsyncResultFromState(state));
        }

        public override int Complete(IAsyncResult result) => _flow.ValidateAndExecute<int, CompleteState>(new CompleteState(), result);

        public override bool IsReady { get; }

        protected override void Dispose(bool disposing)
        {
        }

        private IAsyncResult CreateAsyncResultFromState(object state)
        {
            var moq = new Mock<IAsyncResult>();
            moq.Setup(f => f.AsyncState).Returns(state);
            return moq.Object;
        }
    }
}