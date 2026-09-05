
using System.Data;
using System.Data.SQLite;
using System.Text.Json;
using Modules.DiagnosticContext;
using UniversalWorkflowFramework;

namespace Common.Diagnostics
{

    public class SqliteIsolatedStorage : IIsolatedStorage, IDisposable
    {
        private readonly string _connectionString;
        private SQLiteConnection? _connection;
        private bool _disposed;

        public SqliteIsolatedStorage(string? dbPath = null)
        {
            dbPath ??= GetDefaultDbPath();

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _connectionString = $"Data Source={dbPath};Cache=Shared";
        }

        public void Initialize()
        {
            _connection = new SQLiteConnection(_connectionString);
            _connection.Open();

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Logs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Level TEXT NOT NULL,
                    Category INTEGER NOT NULL,
                    Module TEXT NOT NULL,
                    Message TEXT NOT NULL,
                    SourceFile TEXT,
                    SourceLine INTEGER,
                    SourceMember TEXT,
                    Exception TEXT,
                    BatchId TEXT
                );

                CREATE INDEX IF NOT EXISTS IX_Logs_Timestamp ON Logs(Timestamp);
                CREATE INDEX IF NOT EXISTS IX_Logs_Level ON Logs(Level);
                CREATE INDEX IF NOT EXISTS IX_Logs_Category ON Logs(Category);
                CREATE INDEX IF NOT EXISTS IX_Logs_Module ON Logs(Module);
                CREATE INDEX IF NOT EXISTS IX_Logs_BatchId ON Logs(BatchId);
            ";
            cmd.ExecuteNonQuery();
        }

        public void Write(LogEntry entry)
        {
            EnsureConnection();

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Logs (Timestamp, Level, Category, Module, Message,
                                  SourceFile, SourceLine, SourceMember, Exception, BatchId)
                VALUES (@Timestamp, @Level, @Category, @Module, @Message,
                        @SourceFile, @SourceLine, @SourceMember, @Exception, @BatchId)";

            cmd.Parameters.AddWithValue("@Timestamp", entry.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@Level", entry.Level);
            cmd.Parameters.AddWithValue("@Category", (long)entry.Category);
            cmd.Parameters.AddWithValue("@Module", entry.Module);
            cmd.Parameters.AddWithValue("@Message", entry.Message);
            cmd.Parameters.AddWithValue("@SourceFile", (object?)entry.SourceFile ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SourceLine", (object?)entry.SourceLine ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SourceMember", (object?)entry.SourceMember ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Exception", SerializeException(entry.Exception) ?? (object)DBNull.Value);

            var batchId = entry.Extensions.TryGetValue("BatchId", out var bid) ? bid?.ToString() : null;
            cmd.Parameters.AddWithValue("@BatchId", (object?)batchId ?? DBNull.Value);

            cmd.ExecuteNonQuery();
        }

        public void WriteBatch(IEnumerable<LogEntry> entries)
        {
            EnsureConnection();

            var batch = entries.ToList();
            if (batch.Count == 0) return;

            const int chunkSize = 50;

            using var transaction = _connection!.BeginTransaction();

            for (int chunkStart = 0; chunkStart < batch.Count; chunkStart += chunkSize)
            {
                var chunk = batch.Skip(chunkStart).Take(chunkSize).ToList();
                using var cmd = _connection.CreateCommand();

                var sb = new System.Text.StringBuilder();
                sb.Append("INSERT INTO Logs (Timestamp, Level, Category, Module, Message, ");
                sb.Append("SourceFile, SourceLine, SourceMember, Exception, BatchId) VALUES ");

                var valueRows = new List<string>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    valueRows.Add($"(@Ts{i}, @Lv{i}, @Cat{i}, @Mod{i}, @Msg{i}, " +
                                  $"@Sf{i}, @Sl{i}, @Sm{i}, @Ex{i}, @Bid{i})");
                }
                sb.Append(string.Join(", ", valueRows));
                cmd.CommandText = sb.ToString();

                for (int i = 0; i < chunk.Count; i++)
                {
                    var entry = chunk[i];
                    cmd.Parameters.AddWithValue($"@Ts{i}", entry.Timestamp.ToString("O"));
                    cmd.Parameters.AddWithValue($"@Lv{i}", entry.Level);
                    cmd.Parameters.AddWithValue($"@Cat{i}", (long)entry.Category);
                    cmd.Parameters.AddWithValue($"@Mod{i}", entry.Module);
                    cmd.Parameters.AddWithValue($"@Msg{i}", entry.Message);
                    cmd.Parameters.AddWithValue($"@Sf{i}", (object?)entry.SourceFile ?? DBNull.Value);
                    cmd.Parameters.AddWithValue($"@Sl{i}", (object?)entry.SourceLine ?? DBNull.Value);
                    cmd.Parameters.AddWithValue($"@Sm{i}", (object?)entry.SourceMember ?? DBNull.Value);
                    cmd.Parameters.AddWithValue($"@Ex{i}", SerializeException(entry.Exception) ?? (object)DBNull.Value);
                    var bid = entry.Extensions.TryGetValue("BatchId", out var batchIdVal) ? batchIdVal?.ToString() : null;
                    cmd.Parameters.AddWithValue($"@Bid{i}", (object?)bid ?? DBNull.Value);
                }

                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }


        public IEnumerable<LogEntry> Query(LogQueryFilter filter)
        {
            EnsureConnection();

            var sql = new System.Text.StringBuilder("SELECT * FROM Logs WHERE 1=1");
            var parameters = new List<SQLiteParameter>();

            if (!string.IsNullOrEmpty(filter.Path))
            {
                var paths = filter.Path.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (paths.Length > 0)
                {
                    var categoryConditions = new List<string>();
                    foreach (var path in paths)
                    {
                        var category = LogCategoryRegistry.Resolve(path);
                        if (category != LogCategory.None)
                        {
                            var paramName = $"@Category{(ulong)category}";
                            categoryConditions.Add($"(Category & {paramName}) = {paramName}");
                            parameters.Add(new SQLiteParameter(paramName, (long)category));
                        }
                    }

                    if (categoryConditions.Count > 0)
                    {
                        sql.Append(" AND (").Append(string.Join(" OR ", categoryConditions)).Append(")");
                    }
                }
            }

            if (!string.IsNullOrEmpty(filter.Level))
            {
                var levels = filter.Level.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (levels.Length > 0)
                {
                    var levelParams = new List<string>();
                    for (int i = 0; i < levels.Length; i++)
                    {
                        var paramName = $"@Level{i}";
                        levelParams.Add($"Level = {paramName}");
                        parameters.Add(new SQLiteParameter(paramName, levels[i].ToUpperInvariant()));
                    }
                    sql.Append(" AND (").Append(string.Join(" OR ", levelParams)).Append(")");
                }
            }

            var fromTime = filter.GetFromTime();
            if (fromTime.HasValue)
            {
                sql.Append(" AND Timestamp >= @FromTime");
                parameters.Add(new SQLiteParameter("@FromTime", fromTime.Value.ToString("O")));
            }

            if (filter.ToTime.HasValue)
            {
                sql.Append(" AND Timestamp <= @ToTime");
                parameters.Add(new SQLiteParameter("@ToTime", filter.ToTime.Value.ToString("O")));
            }

            if (!string.IsNullOrEmpty(filter.Keyword))
            {
                sql.Append(" AND Message LIKE @Keyword");
                parameters.Add(new SQLiteParameter("@Keyword", $"%{filter.Keyword}%"));
            }

            if (!string.IsNullOrEmpty(filter.BatchId))
            {
                sql.Append(" AND BatchId = @BatchId");
                parameters.Add(new SQLiteParameter("@BatchId", filter.BatchId));
            }

            sql.Append(" ORDER BY Timestamp DESC");
            sql.Append(" LIMIT @Limit");
            parameters.Add(new SQLiteParameter("@Limit", filter.Limit));

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sql.ToString();
            cmd.Parameters.AddRange(parameters.ToArray());

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return ReadEntry(reader);
            }
        }

        public int Count()
        {
            EnsureConnection();

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Logs";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void Clear()
        {
            EnsureConnection();

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM Logs";
            cmd.ExecuteNonQuery();
        }

        public void Close()
        {
            if (_connection != null && _connection.State != ConnectionState.Closed)
            {
                _connection.Close();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Close();
                _connection?.Dispose();
                _connection = null;
                _disposed = true;
            }
        }

        public void ClearByBatchId(string batchId)
        {
            EnsureConnection();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM Logs WHERE BatchId = @BatchId";
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            cmd.ExecuteNonQuery();
        }

        public List<LogEntry> QueryByBatchId(string batchId)
        {
            EnsureConnection();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT * FROM Logs WHERE BatchId = @BatchId ORDER BY Timestamp ASC";
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            using var reader = cmd.ExecuteReader();
            var results = new List<LogEntry>();
            while (reader.Read())
            {
                results.Add(ReadEntry(reader));
            }
            return results;
        }

        public int CountByBatchId(string batchId)
        {
            EnsureConnection();
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Logs WHERE BatchId = @BatchId";
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }


        private void EnsureConnection()
        {
            if (_connection == null || _connection.State != ConnectionState.Open)
            {
                Initialize();
            }
        }

        private static LogEntry ReadEntry(SQLiteDataReader reader)
        {
            return new LogEntry
            {
                Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("Timestamp"))),
                Level = reader.GetString(reader.GetOrdinal("Level")),
                Category = (LogCategory)reader.GetInt64(reader.GetOrdinal("Category")),
                Module = reader.GetString(reader.GetOrdinal("Module")),
                Message = reader.GetString(reader.GetOrdinal("Message")),
                SourceFile = reader.IsDBNull(reader.GetOrdinal("SourceFile"))
                    ? null : reader.GetString(reader.GetOrdinal("SourceFile")),
                SourceLine = reader.IsDBNull(reader.GetOrdinal("SourceLine"))
                    ? null : reader.GetInt32(reader.GetOrdinal("SourceLine")),
                SourceMember = reader.IsDBNull(reader.GetOrdinal("SourceMember"))
                    ? null : reader.GetString(reader.GetOrdinal("SourceMember")),
                Exception = reader.IsDBNull(reader.GetOrdinal("Exception"))
                    ? null : DeserializeException(reader.GetString(reader.GetOrdinal("Exception")))
            };
        }


        private static string? SerializeException(Exception? ex)
        {
            if (ex == null) return null;

            try
            {
                var data = new Dictionary<string, object?>
                {
                    ["Type"] = ex.GetType().FullName,
                    ["Message"] = ex.Message,
                    ["StackTrace"] = ex.StackTrace,
                    ["Source"] = ex.Source,
                    ["HResult"] = ex.HResult
                };

                if (ex.InnerException != null)
                {
                    data["InnerException"] = new Dictionary<string, object?>
                    {
                        ["Type"] = ex.InnerException.GetType().FullName,
                        ["Message"] = ex.InnerException.Message,
                        ["StackTrace"] = ex.InnerException.StackTrace
                    };
                }

                return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                return ex.ToString();
            }
        }

        private static Exception? DeserializeException(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("Type", out var typeEl))
                {
                    var typeName = typeEl.GetString() ?? "System.Exception";
                    var message = root.TryGetProperty("Message", out var msgEl)
                        ? msgEl.GetString() ?? ""
                        : "";
                    var stackTrace = root.TryGetProperty("StackTrace", out var stEl)
                        ? stEl.GetString()
                        : null;

                    var exType = Type.GetType(typeName, throwOnError: false);
                    var ex = exType != null
                        ? (Exception?)Activator.CreateInstance(exType, message)
                        : new Exception(message);

                    if (ex != null && stackTrace != null)
                    {
                        try
                        {
                            var field = typeof(Exception).GetField("_stackTraceString",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            field?.SetValue(ex, stackTrace);
                        }
                        catch { }
                    }

                    return ex ?? new Exception(message);
                }

                return new Exception(json);
            }
            catch
            {
                return new Exception(json);
            }
        }

        private static string GetDefaultDbPath()

        {
            var relativePath = DiagnosticsConfigReader.GetString("storage/sqlite_default_path", typeof(SqliteIsolatedStorage));
            if (!string.IsNullOrEmpty(relativePath))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    relativePath);
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Logs",
                "logs.db");
        }
    }
}
