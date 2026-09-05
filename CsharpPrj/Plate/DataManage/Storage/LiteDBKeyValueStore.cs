#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using DataManage.Abstractions;
using DataManage.Exceptions;

using DataManage.PathManager;
using DataManage.Storage.Helpers;

namespace DataManage.Storage
{

    public sealed class LiteDBKeyValueStore : ITransactionalKeyValueStore
    {
        private readonly AsyncReaderWriterLock _rwLock = new();
        private readonly ISerializer _serializer;
        private LiteDatabase? _database;
        private string? _connectionString;
        private bool _initialized;
        private bool _disposed;

        public string Name { get; }

        public LiteDBKeyValueStore(string name, ISerializer? serializer = null)
        {
            Name = name;
            _serializer = serializer ?? Serializer.Default;
        }

        public void SetConnectionString(string dbPath)
        {
            StoragePathHelper.EnsureDirectory(dbPath);
            _connectionString = $"Filename={dbPath};Connection=shared";
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            using (await _rwLock.WriteLockAsync(cancellationToken))
            {
                if (_initialized) return;
                if (_connectionString == null)
                    throw new StorageInitializationException("连接字符串未设置。", Name);
                _database = new LiteDatabase(_connectionString);
                _initialized = true;
            }
        }

        private ILiteCollection<BsonDocument> GetCollection(string name) =>
            _database?.GetCollection<BsonDocument>(name) ?? throw new ObjectDisposedException(nameof(LiteDBKeyValueStore));

        private BsonDocument ToBsonDocument(IDictionary<string, object> data)
        {
            var doc = new BsonDocument();
            foreach (var kv in data)
            {
                doc[kv.Key] = kv.Value == null
                    ? BsonValue.Null
                    : BsonMapper.Global.Serialize(kv.Value);
            }
            return doc;
        }

        private IDictionary<string, object> FromBsonDocument(BsonDocument doc)
        {
            var dict = new Dictionary<string, object>();
            foreach (var elem in doc)
            {
                dict[elem.Key] = elem.Value.IsNull
                    ? null!
                    : BsonMapper.Global.Deserialize<object>(elem.Value);
            }
            return dict;
        }

        public async Task SaveAsync(string container, IDictionary<string, object> data, CancellationToken cancellationToken = default)
        {
            using (await _rwLock.WriteLockAsync(cancellationToken))
            {
                var col = GetCollection(container);
                var doc = ToBsonDocument(data);
                col.Upsert(doc);
            }
        }

        public async Task SaveBatchAsync(string container, IEnumerable<IDictionary<string, object>> dataList, CancellationToken cancellationToken = default)
        {
            using (await _rwLock.WriteLockAsync(cancellationToken))
            {
                var col = GetCollection(container);
                var docs = dataList.Select(ToBsonDocument);
                col.InsertBulk(docs);
            }
        }

        public async Task<IEnumerable<IDictionary<string, object>>> LoadAllAsync(string container, CancellationToken cancellationToken = default)
        {
            using (await _rwLock.ReadLockAsync(cancellationToken))
            {
                var col = GetCollection(container);
                var result = col.FindAll().Select(FromBsonDocument).ToList();
                return result;
            }
        }

        public async Task<int> DeleteAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default)
        {
            using (await _rwLock.WriteLockAsync(cancellationToken))
            {
                var col = GetCollection(container);
                var bsonValue = keyValue == null ? BsonValue.Null : BsonMapper.Global.Serialize(keyValue);
                var count = col.DeleteMany(doc => doc[keyField] == bsonValue);
                return count;
            }
        }

        public async Task<bool> ExistsAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default)
        {
            using (await _rwLock.ReadLockAsync(cancellationToken))
            {
                var col = GetCollection(container);
                var bsonValue = keyValue == null ? BsonValue.Null : BsonMapper.Global.Serialize(keyValue);
                return col.Exists(doc => doc[keyField] == bsonValue);
            }
        }

        public async Task ExecuteInTransactionAsync(Func<ITransactionContext, Task> action, CancellationToken cancellationToken = default)
        {
            using (await _rwLock.WriteLockAsync(cancellationToken))
            {
                var db = _database ?? throw new ObjectDisposedException(nameof(LiteDBKeyValueStore));
                var context = new LiteDBTransactionContext(db, this);
                try
                {
                    await action(context);
                }
                catch
                {
                    db.Rollback();
                    throw;
                }
            }
        }

        private sealed class LiteDBTransactionContext : ITransactionContext
        {
            private readonly LiteDatabase _db;
            private readonly LiteDBKeyValueStore _store;

            public LiteDBTransactionContext(LiteDatabase db, LiteDBKeyValueStore store)
            {
                _db = db;
                _store = store;
            }

            public Task SaveAsync(string container, IDictionary<string, object> data)
            {
                var col = _db.GetCollection<BsonDocument>(container);
                var doc = _store.ToBsonDocument(data);
                col.Upsert(doc);
                return Task.CompletedTask;
            }

            public Task SaveBatchAsync(string container, IEnumerable<IDictionary<string, object>> dataList)
            {
                var col = _db.GetCollection<BsonDocument>(container);
                var docs = dataList.Select(_store.ToBsonDocument);
                col.InsertBulk(docs);
                return Task.CompletedTask;
            }

            public Task<int> DeleteAsync(string container, string keyField, object keyValue)
            {
                var col = _db.GetCollection<BsonDocument>(container);
                var bsonValue = keyValue == null ? BsonValue.Null : BsonMapper.Global.Serialize(keyValue);
                var count = col.DeleteMany(doc => doc[keyField] == bsonValue);
                return Task.FromResult(count);
            }

            public ValueTask DisposeAsync() => default;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _database?.Dispose();
            _disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}