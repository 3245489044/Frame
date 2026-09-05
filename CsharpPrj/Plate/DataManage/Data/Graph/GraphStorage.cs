#nullable enable

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataManage.Abstractions;

namespace DataManage.Data.Graph
{
    public abstract class SQLiteStorageBase
    {
        protected readonly string _connectionString;
        protected readonly JsonSerializerOptions _jsonOptions;

        protected SQLiteStorageBase(string databasePath)
        {
            if (string.IsNullOrEmpty(databasePath))
                throw new ArgumentNullException(nameof(databasePath));

            var dbDir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
            }

            _connectionString = $"Data Source={databasePath};Version=3;";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
        }

        protected async Task ExecuteNonQueryAsync(string sql, params SQLiteParameter[] parameters)
        {
            await Task.Run(() =>
            {
                using var connection = new SQLiteConnection(_connectionString);
                connection.Open();

                using var command = new SQLiteCommand(sql, connection);
                command.Parameters.AddRange(parameters);
                command.ExecuteNonQuery();

                connection.Close();
            });
        }

        protected async Task<T?> ExecuteScalarAsync<T>(string sql, params SQLiteParameter[] parameters)
        {
            return await Task.Run(() =>
            {
                using var connection = new SQLiteConnection(_connectionString);
                connection.Open();

                using var command = new SQLiteCommand(sql, connection);
                command.Parameters.AddRange(parameters);
                var result = command.ExecuteScalar();

                connection.Close();

                return result != null ? (T?)Convert.ChangeType(result, typeof(T)) : default;
            });
        }

        protected async Task<List<T>> ExecuteReaderAsync<T>(string sql, Func<SQLiteDataReader, T> mapper, params SQLiteParameter[] parameters)
        {
            return await Task.Run(() =>
            {
                using var connection = new SQLiteConnection(_connectionString);
                connection.Open();

                var results = new List<T>();

                using var command = new SQLiteCommand(sql, connection);
                command.Parameters.AddRange(parameters);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(mapper(reader));
                }

                reader.Close();
                connection.Close();

                return results;
            });
        }

        protected async Task ExecuteTransactionAsync(Action<SQLiteConnection> action)
        {
            await Task.Run(() =>
            {
                using var connection = new SQLiteConnection(_connectionString);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                try
                {
                    action(connection);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
                finally
                {
                    connection.Close();
                }
            });
        }

        protected string SerializeObject<T>(T obj)
        {
            return JsonSerializer.Serialize(obj, _jsonOptions);
        }

        protected T? DeserializeObject<T>(string json)
        {
            return !string.IsNullOrEmpty(json) ? JsonSerializer.Deserialize<T>(json, _jsonOptions) : default;
        }

        protected Dictionary<string, object> ObjectToDictionary<T>(T obj)
        {
            if (obj == null)
                return new Dictionary<string, object>();

            var json = JsonSerializer.Serialize(obj, _jsonOptions);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
        }

        protected T DictionaryToObject<T>(IDictionary<string, object> dict)
        {
            var json = JsonSerializer.Serialize(dict, _jsonOptions);
            return JsonSerializer.Deserialize<T>(json)!;
        }
    }

    public abstract class SQLiteKeyValueStorageBase : SQLiteStorageBase, IDataStorage
    {
        protected readonly string _tableName;

        protected SQLiteKeyValueStorageBase(string databasePath, string tableName)
            : base(databasePath)
        {
            _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        }

        public virtual async Task InitializeAsync()
        {
            await CreateStorageTable();
        }

        protected virtual async Task CreateStorageTable()
        {
            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS {0} (
                    Key TEXT PRIMARY KEY,
                    Data TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                )";

            await ExecuteNonQueryAsync(string.Format(createTableSql, _tableName));
        }

        public async Task SaveAsync<T>(string key, T data)
        {
            if (string.IsNullOrEmpty(key))
                throw new Exceptions.StorageException("Key cannot be null or empty");

            var jsonData = SerializeObject(data);

            const string insertSql = @"
                INSERT OR REPLACE INTO {0} (Key, Data, UpdatedAt)
                VALUES (@Key, @Data, @UpdatedAt)";

            await ExecuteNonQueryAsync(
                string.Format(insertSql, _tableName),
                new SQLiteParameter("@Key", key),
                new SQLiteParameter("@Data", jsonData),
                new SQLiteParameter("@UpdatedAt", DateTime.UtcNow.ToString("O"))
            );
        }

        public async Task<T?> LoadAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new Exceptions.StorageException("Key cannot be null or empty");

            const string selectSql = "SELECT Data FROM {0} WHERE Key = @Key";

            var result = await ExecuteScalarAsync<string>(
                string.Format(selectSql, _tableName),
                new SQLiteParameter("@Key", key)
            );

            return DeserializeObject<T>(result);
        }

        public async Task<bool> DeleteAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new Exceptions.StorageException("Key cannot be null or empty");

            const string deleteSql = "DELETE FROM {0} WHERE Key = @Key";

            await ExecuteNonQueryAsync(
                string.Format(deleteSql, _tableName),
                new SQLiteParameter("@Key", key)
            );

            return !await ExistsAsync(key);
        }

        public async Task<bool> ExistsAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new Exceptions.StorageException("Key cannot be null or empty");

            const string existsSql = "SELECT COUNT(*) FROM {0} WHERE Key = @Key";

            var count = await ExecuteScalarAsync<long>(
                string.Format(existsSql, _tableName),
                new SQLiteParameter("@Key", key)
            );

            return count > 0;
        }

        public async Task<IEnumerable<string>> GetKeysAsync()
        {
            const string selectSql = "SELECT Key FROM {0}";

            var keys = await ExecuteReaderAsync<string>(
                string.Format(selectSql, _tableName),
                reader => reader.GetString(0)
            );

            return keys;
        }

        public async Task ClearAsync()
        {
            const string deleteSql = "DELETE FROM {0}";
            await ExecuteNonQueryAsync(string.Format(deleteSql, _tableName));
        }

        public async Task StoreWorkflowDefaultParametersAsync(string workflowId, Dictionary<string, object> parameters)
        {
            var key = $"workflow_params_{workflowId}";
            await SaveAsync(key, parameters);
        }

        public async Task<Dictionary<string, object>> RetrieveWorkflowDefaultParametersAsync(string workflowId)
        {
            var key = $"workflow_params_{workflowId}";
            return await LoadAsync<Dictionary<string, object>>(key) ?? new Dictionary<string, object>();
        }

        public async Task DeleteWorkflowDefaultParametersAsync(string workflowId)
        {
            var key = $"workflow_params_{workflowId}";
            await DeleteAsync(key);
        }

        public async Task<bool> ExistsWorkflowDefaultParametersAsync(string workflowId)
        {
            var key = $"workflow_params_{workflowId}";
            return await ExistsAsync(key);
        }
    }

    public abstract class SQLiteWorkflowStorageBase : SQLiteStorageBase, IWorkflowStorage<string>
    {
        protected readonly string _tableName;

        protected SQLiteWorkflowStorageBase(string databasePath, string tableName = "Workflows")
            : base(databasePath)
        {
            _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        }

        public virtual async Task InitializeAsync()
        {
            await CreateWorkflowsTable();
        }

        protected virtual async Task CreateWorkflowsTable()
        {
            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS {0} (
                    WorkflowId TEXT PRIMARY KEY,
                    WorkflowJson TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                )";

            await ExecuteNonQueryAsync(string.Format(createTableSql, _tableName));
        }

        public async Task StoreAsync(string workflowId, string workflowJson)
        {
            if (string.IsNullOrEmpty(workflowId))
                throw new ArgumentException("WorkflowId cannot be null or empty", nameof(workflowId));

            if (string.IsNullOrEmpty(workflowJson))
                throw new ArgumentException("WorkflowJson cannot be null or empty", nameof(workflowJson));

            const string insertSql = @"
                INSERT OR REPLACE INTO {0} (WorkflowId, WorkflowJson, CreatedAt, UpdatedAt)
                VALUES (@WorkflowId, @WorkflowJson, 
                    COALESCE((SELECT CreatedAt FROM {0} WHERE WorkflowId = @WorkflowId), @CreatedAt), 
                    @UpdatedAt)";

            await ExecuteNonQueryAsync(
                string.Format(insertSql, _tableName),
                new SQLiteParameter("@WorkflowId", workflowId),
                new SQLiteParameter("@WorkflowJson", workflowJson),
                new SQLiteParameter("@CreatedAt", DateTime.UtcNow.ToString("O")),
                new SQLiteParameter("@UpdatedAt", DateTime.UtcNow.ToString("O"))
            );
        }

        public async Task<string?> RetrieveAsync(string workflowId)
        {
            if (string.IsNullOrEmpty(workflowId))
                throw new ArgumentException("WorkflowId cannot be null or empty", nameof(workflowId));

            const string selectSql = "SELECT WorkflowJson FROM {0} WHERE WorkflowId = @WorkflowId";

            return await ExecuteScalarAsync<string>(
                string.Format(selectSql, _tableName),
                new SQLiteParameter("@WorkflowId", workflowId)
            );
        }

        public async Task DeleteAsync(string workflowId)
        {
            if (string.IsNullOrEmpty(workflowId))
                throw new ArgumentException("WorkflowId cannot be null or empty", nameof(workflowId));

            const string deleteSql = "DELETE FROM {0} WHERE WorkflowId = @WorkflowId";

            await ExecuteNonQueryAsync(
                string.Format(deleteSql, _tableName),
                new SQLiteParameter("@WorkflowId", workflowId)
            );
        }

        public async Task<bool> ExistsAsync(string workflowId)
        {
            if (string.IsNullOrEmpty(workflowId))
                throw new ArgumentException("WorkflowId cannot be null or empty", nameof(workflowId));

            const string existsSql = "SELECT COUNT(*) FROM {0} WHERE WorkflowId = @WorkflowId";

            var count = await ExecuteScalarAsync<long>(
                string.Format(existsSql, _tableName),
                new SQLiteParameter("@WorkflowId", workflowId)
            );

            return count > 0;
        }

        public async Task<List<string>> GetAllWorkflowIdsAsync()
        {
            const string selectSql = "SELECT WorkflowId FROM {0} ORDER BY UpdatedAt DESC";

            var workflowIds = await ExecuteReaderAsync<string>(
                string.Format(selectSql, _tableName),
                reader => reader.GetString(0)
            );

            return workflowIds.ToList();
        }

        public async Task ClearAllWorkflowsAsync()
        {
            const string deleteSql = "DELETE FROM {0}";
            await ExecuteNonQueryAsync(string.Format(deleteSql, _tableName));
        }
    }
}