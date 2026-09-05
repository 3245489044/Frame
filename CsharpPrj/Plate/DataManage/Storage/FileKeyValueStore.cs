#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataManage.Abstractions;
using DataManage.Exceptions;

using DataManage.PathManager;
using DataManage.Storage.Helpers;

namespace DataManage.Storage
{

    public sealed class FileKeyValueStore : ITransactionalKeyValueStore
    {
        private readonly AsyncLock _asyncLock = new();
        private readonly ISerializer _serializer;
        private string? _baseDirectory;

        public string Name { get; }

        public FileKeyValueStore(string name, ISerializer? serializer = null)
        {
            Name = name;
            _serializer = serializer ?? Serializer.Default;
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_baseDirectory == null)
                throw new StorageInitializationException("存储路径未设置。", Name);
            StoragePathHelper.EnsureDirectory(_baseDirectory);
            return Task.CompletedTask;
        }

        public void SetBaseDirectory(string path)
        {
            _baseDirectory = path;
        }

        private string GetFilePath(string container) => Path.Combine(_baseDirectory!, $"{container}.jsonl");

        private async Task<List<Dictionary<string, object>>> ReadAllAsync(string container, CancellationToken cancellationToken)
        {
            var path = GetFilePath(container);
            if (!File.Exists(path)) return new List<Dictionary<string, object>>();

            var result = new List<Dictionary<string, object>>();
            using var reader = new StreamReader(path);
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var dict = _serializer.Deserialize<Dictionary<string, object>>(line);
                result.Add(dict);
            }
            return result;
        }

        private async Task RewriteAsync(string container, IEnumerable<Dictionary<string, object>> data, CancellationToken cancellationToken)
        {
            var path = GetFilePath(container);
            using var writer = new StreamWriter(path, append: false);
            foreach (var dict in data)
            {
                var line = _serializer.Serialize(dict);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            }
        }

        private static bool AreValuesEqual(object? a, object? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;

            string strA = Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture);
            string strB = Convert.ToString(b, System.Globalization.CultureInfo.InvariantCulture);
            return string.Equals(strA, strB, StringComparison.Ordinal);
        }

        public async Task SaveAsync(string container, IDictionary<string, object> data, CancellationToken cancellationToken = default)
        {
            const string defaultKeyField = "Id";
            object? keyValue = data.TryGetValue(defaultKeyField, out var val) ? val : null;

            using (await _asyncLock.LockAsync(cancellationToken))
            {
                var all = await ReadAllAsync(container, cancellationToken);
                if (keyValue != null)
                {
                    all.RemoveAll(d => d.TryGetValue(defaultKeyField, out var existing) && AreValuesEqual(existing, keyValue));
                }
                all.Add(new Dictionary<string, object>(data));
                await RewriteAsync(container, all, cancellationToken);
            }
        }

        public async Task SaveBatchAsync(string container, IEnumerable<IDictionary<string, object>> dataList, CancellationToken cancellationToken = default)
        {
            var list = dataList.ToList();
            if (list.Count == 0) return;

            using (await _asyncLock.LockAsync(cancellationToken))
            {
                var all = await ReadAllAsync(container, cancellationToken);
                foreach (var data in list)
                {
                    if (data.TryGetValue("Id", out var keyValue) && keyValue != null)
                    {
                        all.RemoveAll(d => d.TryGetValue("Id", out var existing) && AreValuesEqual(existing, keyValue));
                    }
                    all.Add(new Dictionary<string, object>(data));
                }
                await RewriteAsync(container, all, cancellationToken);
            }
        }

        public async Task<IEnumerable<IDictionary<string, object>>> LoadAllAsync(string container, CancellationToken cancellationToken = default)
        {
            using (await _asyncLock.LockAsync(cancellationToken))
            {
                var all = await ReadAllAsync(container, cancellationToken);
                return all.Select(d => (IDictionary<string, object>)d);
            }
        }

        public async Task<int> DeleteAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default)
        {
            using (await _asyncLock.LockAsync(cancellationToken))
            {
                var all = await ReadAllAsync(container, cancellationToken);
                int removed = all.RemoveAll(d => d.TryGetValue(keyField, out var val) && AreValuesEqual(val, keyValue));
                if (removed > 0)
                    await RewriteAsync(container, all, cancellationToken);
                return removed;
            }
        }

        public async Task<bool> ExistsAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default)
        {
            using (await _asyncLock.LockAsync(cancellationToken))
            {
                var all = await ReadAllAsync(container, cancellationToken);
                return all.Any(d => d.TryGetValue(keyField, out var val) && AreValuesEqual(val, keyValue));
            }
        }

        public async Task ExecuteInTransactionAsync(Func<ITransactionContext, Task> action, CancellationToken cancellationToken = default)
        {
            using (await _asyncLock.LockAsync(cancellationToken))
            {
                var context = new FileTransactionContext(this, cancellationToken);
                try
                {
                    await action(context);
                    await context.CommitAsync();
                }
                catch
                {
                    context.Rollback();
                    throw;
                }
            }
        }

        private sealed class FileTransactionContext : ITransactionContext
        {
            private readonly FileKeyValueStore _store;
            private readonly CancellationToken _cancellationToken;
            private readonly Dictionary<string, List<Dictionary<string, object>>> _pendingChanges = new();
            private readonly Dictionary<string, List<(string keyField, object keyValue)>> _pendingDeletes = new();

            public FileTransactionContext(FileKeyValueStore store, CancellationToken cancellationToken)
            {
                _store = store;
                _cancellationToken = cancellationToken;
            }

            public Task SaveAsync(string container, IDictionary<string, object> data)
            {
                if (!_pendingChanges.TryGetValue(container, out var list))
                {
                    list = new List<Dictionary<string, object>>();
                    _pendingChanges[container] = list;
                }
                list.Add(new Dictionary<string, object>(data));
                return Task.CompletedTask;
            }

            public Task SaveBatchAsync(string container, IEnumerable<IDictionary<string, object>> dataList)
            {
                if (!_pendingChanges.TryGetValue(container, out var list))
                {
                    list = new List<Dictionary<string, object>>();
                    _pendingChanges[container] = list;
                }
                list.AddRange(dataList.Select(d => new Dictionary<string, object>(d)));
                return Task.CompletedTask;
            }

            public Task<int> DeleteAsync(string container, string keyField, object keyValue)
            {
                if (!_pendingDeletes.TryGetValue(container, out var list))
                {
                    list = new List<(string, object)>();
                    _pendingDeletes[container] = list;
                }
                list.Add((keyField, keyValue));
                return Task.FromResult(1);
            }

            public async Task CommitAsync()
            {
                foreach (var (container, changes) in _pendingChanges)
                {
                    var all = await _store.ReadAllAsync(container, _cancellationToken);

                    foreach (var delete in _pendingDeletes.GetValueOrDefault<string, List<(string, object)>>(container, new List<(string, object)>()))
                    {
                        all.RemoveAll(d => d.TryGetValue(delete.Item1, out var val) && FileKeyValueStore.AreValuesEqual(val, delete.Item2));
                    }

                    foreach (var data in changes)
                    {
                        if (data.TryGetValue("Id", out var keyValue) && keyValue != null)
                        {
                            all.RemoveAll(d => d.TryGetValue("Id", out var existing) && FileKeyValueStore.AreValuesEqual(existing, keyValue));
                        }
                        all.Add(data);
                    }

                    await _store.RewriteAsync(container, all, _cancellationToken);
                }
            }

            public void Rollback()
            {
                _pendingChanges.Clear();
                _pendingDeletes.Clear();
            }

            public ValueTask DisposeAsync()
            {
                Rollback();
                return default;
            }
        }

        public void Dispose() { }

        public ValueTask DisposeAsync() => default;
    }
}