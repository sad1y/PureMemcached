using System;
using System.Threading.Tasks;

namespace PureMemcached;

/// <summary>
/// base class for containers that will give you a reusable object and you promise to give it back.
/// </summary>
/// <typeparam name="T">Type of object</typeparam>
public interface IObjectPool<T> : IDisposable
{
    public ValueTask<T> RentAsync();
    public ValueTask ReturnAsync(T obj);
}