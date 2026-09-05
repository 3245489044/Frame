#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
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

    internal sealed class SQLiteConnectionManager
    {
        private readonly object _createFileLock = new();
        private string? _connectionString;

        public void SetConnectionString(string dbPath)
        {
            if (Directory.Exists(dbPath))
            {
                dbPath = Path.Combine(dbPath, "default.db");
            }
            
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            lock (_createFileLock)
            {
                if (!File.Exists(dbPath))
                {
                    SQLiteConnection.CreateFile(dbPath);
                }
            }
            _connectionString = $"Data Source={dbPath};Version=3;MultipleReadWrite=True;BusyTimeout=5000;Journal Mode=WAL;";
        }

        public SQLiteConnection CreateConnection()
        {
            if (_connectionString == null)
                throw new StorageInitializationException("连接字符串未设置。");
            
            return new SQLiteConnection(_connectionString);
        }

        public async Task<SQLiteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            if (_connectionString == null)
                throw new StorageInitializationException("连接字符串未设置。");
            
            var conn = new SQLiteConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }

    internal sealed class SQLiteTableMaintainer
    {
        private readonly HashSet<string> _existingTables = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _tableColumns = new(StringComparer.OrdinalIgnoreCase);
        private readonly AsyncLock _lock = new();

        public async Task EnsureTableAsync(SQLiteConnection conn, string tableName, IEnumerable<string> columns, CancellationToken cancellationToken)
        {
            using (await _lock.LockAsync(cancellationToken))
            {
                if (_existingTables.Contains(tableName))
                {
                    await AddMissingColumnsAsync(conn, tableName, columns, cancellationToken);
                    return;
                }

                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name LIMIT 1";
                cmd.Parameters.AddWithValue("@name", tableName);
                var exists = await cmd.ExecuteScalarAsync(cancellationToken) != null;

                var allColumnNames = columns.ToList();

                if (!exists)
                {
                    var colDefs = allColumnNames
                        .Where(c => !c.Equals("Id", StringComparison.OrdinalIgnoreCase))
                        .Select(c => $"[{EscapeColumnName(c)}] TEXT");
                    var allCols = new[] { "[Id] INTEGER PRIMARY KEY AUTOINCREMENT" }.Concat(colDefs);
                    cmd.CommandText = $"CREATE TABLE [{EscapeTableName(tableName)}] ({string.Join(", ", allCols)});";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                    _tableColumns[tableName] = new HashSet<string>(allColumnNames, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    cmd.CommandText = $"PRAGMA table_info([{EscapeTableName(tableName)}])";
                    var existingColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            existingColumnNames.Add(reader.GetString(1));
                        }
                    }

                    foreach (var col in allColumnNames.Where(c => !c.Equals("Id", StringComparison.OrdinalIgnoreCase) && !existingColumnNames.Contains(c)))
                    {
                        cmd.CommandText = $"ALTER TABLE [{EscapeTableName(tableName)}] ADD COLUMN [{EscapeColumnName(col)}] TEXT;";
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                        existingColumnNames.Add(col);
                    }

                    _tableColumns[tableName] = existingColumnNames;
                }

                _existingTables.Add(tableName);
            }
        }

        public async Task AddMissingColumnsAsync(SQLiteConnection conn, string tableName, IEnumerable<string> requiredColumns, CancellationToken cancellationToken)
        {
            using (await _lock.LockAsync(cancellationToken))
            {
                if (!_tableColumns.TryGetValue(tableName, out var existingCols))
                    return;

                foreach (var col in requiredColumns.Where(c => !c.Equals("Id", StringComparison.OrdinalIgnoreCase) && !existingCols.Contains(c)))
                {
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = $"ALTER TABLE [{EscapeTableName(tableName)}] ADD COLUMN [{EscapeColumnName(col)}] TEXT;";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                    existingCols.Add(col);
                }
            }
        }

        public bool TableExists(string tableName) => _existingTables.Contains(tableName);
        public void MarkTableExists(string tableName) => _existingTables.Add(tableName);
        public void Clear()
        {
            _existingTables.Clear();
            _tableColumns.Clear();
        }

        private static string EscapeTableName(string name) => name.Replace("]", "]]").Replace("\"", "\"\"");
        private static string EscapeColumnName(string name) => name.Replace("]", "]]").Replace("\"", "\"\"");
    }

    public sealed class SQLiteKeyValueStore : ITransactionalKeyValueStore
    {
        private readonly AsyncLock _asyncLock = new();
        private readonly ISerializer _serializer;
        private readonly SQLiteConnectionManager _connectionManager = new();
        private readonly SQLiteTableMaintainer _tableMaintainer = new();
        private bool _initialized;
        private bool _disposed;

        public string Name { get; }

        public SQLiteKeyValueStore(string name, ISerializer? serializer = null)
        {
            Name = name;
            _serializer = serializer ?? Serializer.Default;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            using (await _asyncLock.LockAsync(cancellationToken))
            {
                if (_initialized) return;
                _initialized = true;
            }
            await Task.CompletedTask;
        }

        public void SetConnectionString(string dbPath)
        {
            _connectionManager.SetConnectionString(dbPath);
        }

        private async Task SaveBatchInternalAsync(SQLiteConnection conn, SQLiteTransaction? tx, string container, IReadOnlyList<IDictionary<string, object>> dataList, CancellationToken cancellationToken)
        {
            if (dataList.Count == 0) return;

            var first = dataList[0];
            var columns = first.Keys.Where(k => !k.Equals("Id", StringComparison.OrdinalIgnoreCase)).ToList();

            await _tableMaintainer.EnsureTableAsync(conn, container, first.Keys, cancellationToken);
            await _tableMaintainer.AddMissingColumnsAsync(conn, container, columns, cancellationToken);

            const int maxBatchSize = 500;
            for (int i = 0; i < dataList.Count; i += maxBatchSize)
            {
                var batch = dataList.Skip(i).Take(maxBatchSize).ToList();
                var valuePlaceholders = new List<string>();
                var parameters = new List<SQLiteParameter>();

                for (int rowIdx = 0; rowIdx < batch.Count; rowIdx++)
                {
                    var rowPlaceholders = new List<string>();
                    var data = batch[rowIdx];
                    foreach (var col in columns)
                    {
                        var paramName = $"@{col}_{rowIdx}";
                        rowPlaceholders.Add(paramName);
                        var param = new SQLiteParameter(paramName, data.TryGetValue(col, out var val) ? (val ?? DBNull.Value) : DBNull.Value);
                        parameters.Add(param);
                    }
                    valuePlaceholders.Add($"({string.Join(", ", rowPlaceholders)})");
                }

                var sql = $"INSERT INTO [{container}] ({string.Join(", ", columns.Select(c => $"[{c}]"))}) VALUES {string.Join(", ", valuePlaceholders)}";
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                if (tx != null)
                    cmd.Transaction = tx;
                cmd.Parameters.AddRange(parameters.ToArray());
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task SaveAsync(string container, IDictionary<string, object> data, CancellationToken cancellationToken = default)
        {
            await SaveBatchAsync(container, new[] { data }, cancellationToken);
        }

        public async Task SaveBatchAsync(string container, IEnumerable<IDictionary<string, object>> dataList, CancellationToken cancellationToken = default)
        {
            var list = dataList as IReadOnlyList<IDictionary<string, object>> ?? dataList.ToList();
            if (list.Count == 0) return;

            using (await _asyncLock.LockAsync(cancellationToken))
            {
                await using var conn = await _connectionManager.OpenConnectionAsync(cancellationToken);
                await using var tx = await conn.BeginTransactionAsync(cancellationToken) as SQLiteTransaction
                    ?? throw new InvalidOperationException("事务类型不是 SQLiteTransaction");
                try
                {
                    await SaveBatchInternalAsync(conn, tx, container, list, cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                }
                catch
                {
                    await tx.RollbackAsync(cancellationToken);
                    throw;
                }
            }
        }

        public async Task<IEnumerable<IDictionary<string, object>>> LoadAllAsync(string container, CancellationToken cancellationToken = default)
        {
            using (await _asyncLock.LockAsync(cancellationToken))
            {
                await using var conn = await _connectionManager.OpenConnectionAsync(cancellationToken);
                if (!_tableMaintainer.TableExists(container))
                {
                    var cmdCheck = conn.CreateCommand();
                    cmdCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
                    cmdCheck.Parameters.AddWithValue("@name", container);
                    if (Convert.ToInt64(await cmdCheck.ExecuteScalarAsync(cancellationToken)) == 0)
                        return Enumerable.Empty<IDictionary<string, object>>();
                    _tableMaintainer.MarkTableExists(container);
                }

                var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT * FROM [{container}]";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                var result = new List<IDictionary<string, object>>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.GetValue(i);
                    result.Add(row);
                }
                return result;
            }
        }

        public async Task<int> DeleteAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default)
        {
            using (await _asyncLock.LockAsync(cancellationToken))
            {
                await using var conn = await _connectionManager.OpenConnectionAsync(cancellationToken);
                var cmd = conn.CreateCommand();
                cmd.CommandText = $"DELETE FROM [{container}] WHERE [{keyField}] = @value";
                cmd.Parameters.AddWithValue("@value", keyValue ?? DBNull.Value);
                return await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task<bool> ExistsAsync(string container, string keyField, object keyValue, CancellationToken cancellationToken = default)
        {
            using (await _asyncLock.LockAsync(cancellationToken))
            {
                await using var conn = await _connectionManager.OpenConnectionAsync(cancellationToken);
                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT 1 FROM [{container}] WHERE [{keyField}] = @value LIMIT 1";
                    cmd.Parameters.AddWithValue("@value", keyValue ?? DBNull.Value);
                    return await cmd.ExecuteScalarAsync(cancellationToken) != null;
                }
                catch (SQLiteException)
                {
                    return false;
                }
            }
        }

        public async Task ExecuteInTransactionAsync(Func<ITransactionContext, Task> action, CancellationToken cancellationToken = default)
        {
            using (await _asyncLock.LockAsync(cancellationToken))
            {
                await using var conn = await _connectionManager.OpenConnectionAsync(cancellationToken);
                await using var tx = await conn.BeginTransactionAsync(cancellationToken) as SQLiteTransaction
                    ?? throw new InvalidOperationException("事务类型不是 SQLiteTransaction");
                var context = new SQLiteTransactionContext(conn, tx, this);
                try
                {
                    await action(context);
                    await tx.CommitAsync(cancellationToken);
                }
                catch
                {
                    await tx.RollbackAsync(cancellationToken);
                    throw;
                }
            }
        }

        private sealed class SQLiteTransactionContext : ITransactionContext
        {
            private readonly SQLiteConnection _conn;
            private readonly SQLiteTransaction _tx;
            private readonly SQLiteKeyValueStore _store;

            public SQLiteTransactionContext(SQLiteConnection conn, SQLiteTransaction tx, SQLiteKeyValueStore store)
            {
                _conn = conn;
                _tx = tx;
                _store = store;
            }

            public async Task SaveAsync(string container, IDictionary<string, object> data)
            {
                await _store.SaveBatchInternalAsync(_conn, _tx, container, new[] { data }, CancellationToken.None);
            }

            public async Task SaveBatchAsync(string container, IEnumerable<IDictionary<string, object>> dataList)
            {
                var list = dataList as IReadOnlyList<IDictionary<string, object>> ?? dataList.ToList();
                await _store.SaveBatchInternalAsync(_conn, _tx, container, list, CancellationToken.None);
            }

            public Task<int> DeleteAsync(string container, string keyField, object keyValue)
            {
                var cmd = _conn.CreateCommand();
                cmd.Transaction = _tx;
                cmd.CommandText = $"DELETE FROM [{container}] WHERE [{keyField}] = @value";
                cmd.Parameters.AddWithValue("@value", keyValue ?? DBNull.Value);
                return cmd.ExecuteNonQueryAsync();
            }

            public ValueTask DisposeAsync() => default;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tableMaintainer.Clear();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}