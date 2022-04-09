using System;
using System.Collections;
using FluentAssertions;

namespace PureMemcached.Test;

public partial class MemcachedClientTest
{
    /// <summary>
    /// facilities for client testing
    /// </summary>
    private class ExecutionFlow : IEnumerable
    {
        private readonly Queue _states;

        public ExecutionFlow()
        {
            _states = new Queue();
        }

        public void Add(object state)
        {
            _states.Enqueue(state);
        }

        public void Validate<TState>(TState state)
        {
            var expectedState = TakeNextState();
            state.Should().Be(expectedState);
        }

        public T ValidateAndExecute<T, TState>(TState state, params object[] args) where TState : IExecutableState<T>
        {
            var expectedState = TakeNextState();
            state.Should().Be(expectedState);
            var executableState = (IExecutableState<T>)expectedState;
            return executableState.Execute(args);
        }

        public void AssertThatThereIsNoStatesLeft()
        {
            _states.Count.Should().Be(0);
        }

        public IEnumerator GetEnumerator() => _states.GetEnumerator();

        private object TakeNextState() => _states.Dequeue();
    }

    private interface IExecutableState<out T>
    {
        T Execute(object[] args);
    }

    private class BeginSendState
    {
        private readonly byte[] _buffer;
        private readonly int _offset;
        private readonly int _size;

        public BeginSendState(byte[] buffer, int offset, int size)
        {
            _buffer = buffer;
            _offset = offset;
            _size = size;
        }

        public bool Equals(BeginSendState other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            var slice = _buffer.AsSpan().Slice(_offset, _size);
            return slice.SequenceEqual(other._buffer) && _offset == other._offset && _size == other._size;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((BeginSendState)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_buffer, _offset, _size);
        }

        public static bool operator ==(BeginSendState left, BeginSendState right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(BeginSendState left, BeginSendState right)
        {
            return !Equals(left, right);
        }

        public override string ToString()
        {
            return $"{nameof(BeginSendState)} {nameof(_offset)}: {_offset}, {nameof(_size)}: {_size}";
        }
    }

    private class BeginReceiveState : IEquatable<BeginReceiveState>
    {
        private readonly int _offset;
        private readonly int _size;

        public BeginReceiveState(int offset, int size)
        {
            _offset = offset;
            _size = size;
        }

        public bool Equals(BeginReceiveState other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return _offset == other._offset && _size == other._size;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((BeginReceiveState)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_offset, _size);
        }

        public static bool operator ==(BeginReceiveState left, BeginReceiveState right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(BeginReceiveState left, BeginReceiveState right)
        {
            return !Equals(left, right);
        }
    }

    private class CompleteReceiveState : CompleteState
    {
        private readonly int _read;
        private readonly byte[] _buffer;

        public CompleteReceiveState(int read, byte[] buffer) : base(read)
        {
            _read = read;
            _buffer = buffer;
        }

        public override int Execute(object[] args)
        {
            var result = (IAsyncResult)args[0];
            var state = (MemcachedClient.ReceiveState)result.AsyncState;
            _buffer.CopyTo(state.Buffer.AsSpan());
            return _read;
        }
    }

    private class ThrowsExceptionState : IExecutableState<object>
    {
        public object Execute(object[] args) => throw new Exception();
    }

    private class CompleteState : IExecutableState<int>, IEquatable<CompleteState>
    {
        private readonly int _result;

        public CompleteState(int result = 0)
        {
            _result = result;
        }

        public virtual int Execute(object[] args) => _result;

        public bool Equals(CompleteState other)
        {
            if (ReferenceEquals(null, other)) return false;
            return true; // because we only care about type 
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (!obj.GetType().IsAssignableTo(GetType())) return false;
            return Equals((CompleteState)obj);
        }

        public override int GetHashCode()
        {
            return _result;
        }

        public static bool operator ==(CompleteState left, CompleteState right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(CompleteState left, CompleteState right)
        {
            return !Equals(left, right);
        }
    }
}