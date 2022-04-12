
using System.Threading.Tasks;

namespace PureMemcached;

/// <summary>
/// Create typed connection for memcached client
/// </summary>
public interface IConnectionFactory
{
    Task<Connection> CreateAsync();
}