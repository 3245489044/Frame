#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataManage.Abstractions
{
    public interface IKeyValueStore : IAsyncDisposable, IDisposable
    {
        string Name { get; }
        Task InitializeAsync(CancellationToken cancellationToken = default);
        Task SaveAsync(string container, IDictionary<string, object> data, CancellationToken cancellationToken = default);
        Task SaveBatchAsync(string container, IEnumerable<IDictionary<string, object>> dataList, CancellationToken cancellationToken = default);
        Task<IEnumerable<IDictionary<string, object>>> LoadAllAsync(string container, CancellationToken cancellationToken = default);
        Task<int> DeleteAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default);
    }

    public interface ITransactionalKeyValueStore : IKeyValueStore
    {
        Task ExecuteInTransactionAsync(Func<ITransactionContext, Task> action, CancellationToken cancellationToken = default);
    }

    public interface ITransactionContext : IAsyncDisposable
    {
        Task SaveAsync(string container, IDictionary<string, object> data);
        Task SaveBatchAsync(string container, IEnumerable<IDictionary<string, object>> dataList);
        Task<int> DeleteAsync(string container, string keyField, object keyValue);
    }
}