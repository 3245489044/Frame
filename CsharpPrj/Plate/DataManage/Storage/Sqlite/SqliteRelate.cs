#nullable disable
#region USING
using System.Collections.Concurrent;
using System.Threading;
using System.Data.SQLite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
#endregion

namespace UniversalWorkflowFramework.Storage
{
    public static class SqliteRelate
    {
        public static class DataUniversal
        {
            private const string ConnectionStringPrefix = "Data Source=";
            private const string ConnectionStringSuffix = ";Version=3;";

            // 写门（Stage 2 收敛）：按库（连接串）各自的单写门 SemaphoreSlim(1,1) + 有界等待，取代旧「全局 object Gate 无界 Monitor」。
            // 依据《2026-09-03-db-read-hang-arch-refactor.md》：同一 DB 的写串行化应落在库级（对齐 SqliteWriteChannel 单写连接思想），
            // 不同 DB 文件不互串；等待有界（gateWaitMs），杜绝写路径在进程门上无限阻塞。读路径已独立（ExecuteQueryCore 不进此门）。
            private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteGates =
                new(StringComparer.Ordinal);

            private static readonly int WriteGateWaitMs = 5000;

            private static SemaphoreSlim GateFor(string connectionString)
                => WriteGates.GetOrAdd(connectionString, static _ => new SemaphoreSlim(1, 1));

            public static string DataBasePath = string.Empty;

            /// <summary>由全局 DataBasePath 构造连接串（保留旧语义）。</summary>
            public static string BuildConnectionString() =>
                $"{ConnectionStringPrefix}{DataBasePath}{ConnectionStringSuffix}";

            public static SQLiteConnection SetConnect() => SetConnect(BuildConnectionString());

            public static SQLiteConnection SetConnect(string connectionString)
            {
                var conn = new SQLiteConnection(connectionString);
                try
                {
                    conn.Open();
                    // 并发写同一 agent.db（TaskPool ur_tasks 连接 + 持久化骨干连接）避免 database is locked：
                    // busy_timeout 让写事务等待锁释放（默认 0 立即失败）；journal_mode=WAL 允许单写者 + 多读者并发。
                    ExecutePragma(conn, "PRAGMA busy_timeout = 5000;");
                    ExecutePragma(conn, "PRAGMA journal_mode = WAL;");
                }
                catch (Exception ex) { throw new Exception($"数据库连接失败: {ex.Message}", ex); }
                return conn;
            }

            /// <summary>只读连接：仅设 <c>busy_timeout</c>（有界），不重发 <c>PRAGMA journal_mode=WAL</c>。
            /// WAL 为库级持久化模式，首次由任一写连接设定后常驻；读连接每次重发该 pragma 需库级写权，
            /// 会与在途写者互等（此前读慢/饿死根因之一）。WAL 下读者拿独立快照、可与单写者并发，无需进程写门。</summary>
            public static SQLiteConnection SetReadConnect(string connectionString)
            {
                var conn = new SQLiteConnection(connectionString);
                try
                {
                    conn.Open();
                    // 仅 busy_timeout：读在 WAL 下基本不阻塞；若库仍处 rollback journal（从未被写连接转 WAL），
                    // 与在途写者同库的读由 busy_timeout 有界失败返回，而非无限阻塞。
                    ExecutePragma(conn, "PRAGMA busy_timeout = 5000;");
                }
                catch (Exception ex) { throw new Exception($"数据库读连接失败: {ex.Message}", ex); }
                return conn;
            }

            private static void ExecutePragma(SQLiteConnection conn, string sql)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }

            public static void ExecuteNonQuery(string sql, Dictionary<string, object> parameters = null) =>
                ExecuteNonQuery(BuildConnectionString(), sql, parameters);

            public static void ExecuteNonQuery(string connectionString, string sql, Dictionary<string, object> parameters = null) =>
                ExecuteSQL(connectionString, sql, parameters, cmd => cmd.ExecuteNonQuery());

            public static void ExecuteSQL(string query, Dictionary<string, object> parameters, Action<SQLiteCommand> action) =>
                ExecuteSQL(BuildConnectionString(), query, parameters, action);

            public static void ExecuteSQL(string connectionString, string query, Dictionary<string, object> parameters, Action<SQLiteCommand> action)
            {
                // 写路径：按库单写门 + 有界等待（见 WriteGates 说明）。不做应用层重试——
                // SQLite 自身 busy_timeout(5s) 已吸收真正短暂的瞬态竞争；busy_timeout 到期仍 busy =
                // 写锁被长持（非瞬态），应快速失败暴露，由上层归类（execution_failure/引擎 TO），
                // 而非在存储层指数退避空转（会把单次写拖到 ~30s，且掩盖真实的写锁长持者）。
                var gate = GateFor(connectionString);
                bool acquired;
                try { acquired = gate.Wait(WriteGateWaitMs); }
                catch { acquired = false; } // 信号量 Wait 异常按未获得处理
                if (!acquired)
                {
                    // 写门等待超时：附加当前库写权持有者（跨两套写面 registry 可见 SqliteWriteChannel/其它 DataUniversal 长持方）。
                    throw new InvalidOperationException(
                        $"SQLite 写门等待超时({WriteGateWaitMs}ms)，库写面疑似被长持，写未执行。DB: {connectionString}"
                        + SqliteWriteOwnerRegistry.Describe(connectionString));
                }
                // 门内写执行期登记本库写权持有者（DataUniversal 写面），供其它写面/自身并发定位长持方。
                var htok = SqliteWriteOwnerRegistry.Enter(connectionString, "DataUniversal");
                try
                {
                    using var conn = SetConnect(connectionString);
                    using var cmd = new SQLiteCommand(query, conn);
                    if (parameters != null)
                        foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Key, p.Value);
                    try
                    {
                        action(cmd);
                    }
                    catch (Exception ex) when (IsBusy(ex))
                    {
                        // busy_timeout(5s) 已等待仍 busy → 写锁长持，非瞬态；快速失败暴露（上层归类 execution_failure/TO）。
                        // 附加 registry 中当前仍持写权者（排除自身）——即真正长持 SqliteWriteChannel 事务/其它写连接。
                        throw new Exception(
                            $"SQLite 写锁竞争（database is locked/busy），疑似他处连接长持写事务，写未执行。DB: {connectionString} | SQL: {query}"
                            + SqliteWriteOwnerRegistry.Describe(connectionString, htok), ex);
                    }
                    catch (Exception ex)
                    {
                        // 携带实际 SQL 供排障。
                        throw new Exception($"SQL执行失败: {ex.Message} | SQL: {query}", ex);
                    }
                }
                finally
                {
                    SqliteWriteOwnerRegistry.Exit(connectionString, htok);
                    gate.Release();
                }
            }

            /// <summary>判定异常是否 SQLite 写锁竞争（database is locked / busy），以决定是否退避重试。</summary>
            private static bool IsBusy(Exception ex)
            {
                var msg = ex.Message;
                return msg.IndexOf("database is locked", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("database is busy", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("is locked", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            public static object ExecuteScalar(string query, Dictionary<string, object> parameters = null) =>
                ExecuteScalar(BuildConnectionString(), query, parameters);

            public static object ExecuteScalar(string connectionString, string query, Dictionary<string, object> parameters = null)
            {
                object result = null;
                ExecuteSQL(connectionString, query, parameters, cmd => result = cmd.ExecuteScalar());
                return result;
            }

            public static void ExecuteQueryCore(string query, Action<SQLiteDataReader> rowAction, bool readAllRows = true, Dictionary<string, object> parameters = null) =>
                ExecuteQueryCore(BuildConnectionString(), query, rowAction, readAllRows, parameters);

            public static void ExecuteQueryCore(string connectionString, string query, Action<SQLiteDataReader> rowAction, bool readAllRows = true, Dictionary<string, object> parameters = null)
            {
                // 读路径（SELECT）不进进程写门：WAL 下读者拿独立快照、可与单写者并发。若仍经旧「进程写门」排队，
                // 一旦后台写者长持写面，所有该库读（UI DbReadTableMarkdown / Agent ResolveInputs ur_nodes）将在
                // 进程门上无限饿死（本 bug 的直接承担机制）。读用只读连接（仅 busy_timeout，不抢 WAL 写权），有界失败而非无界阻塞。
                using var conn = SetReadConnect(connectionString);
                using var cmd = new SQLiteCommand(query, conn);
                if (parameters != null)
                    foreach (var p in parameters)
                        cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
                try
                {
                    using var r = cmd.ExecuteReader();
                    if (readAllRows) while (r.Read()) rowAction(r);
                    else if (r.Read()) rowAction(r);
                }
                catch (Exception ex) { throw new Exception($"SQL查询执行失败: {ex.Message}\nSQL: {query}", ex); }
            }

            public static List<string> GetTableColumnDefinitions(string tableName)
            {
                var defs = new List<string>();
                ExecuteQueryCore(string.Format("PRAGMA table_info({0});", tableName), r =>
                {
                    string name = r["name"].ToString();
                    string type = r["type"].ToString();
                    string notnull = (long)r["notnull"] == 1 ? "NOT NULL" : "";
                    string dflt = r["dflt_value"] != DBNull.Value ? $"DEFAULT {r["dflt_value"]}" : "";
                    string pk = (long)r["pk"] == 1 ? "PRIMARY KEY" : "";
                    defs.Add($"{name} {type} {notnull} {dflt} {pk}".Trim());
                });
                return defs;
            }
        }

        public static class DataCreate
        {
            public static void CreateDataBase()
            {
                if (!File.Exists(DataUniversal.DataBasePath))
                    SQLiteConnection.CreateFile(DataUniversal.DataBasePath);
            }

            public static void CreateDataBase(string databasePath)
            {
                if (!File.Exists(databasePath))
                    SQLiteConnection.CreateFile(databasePath);
            }

            public static bool DoesTableExist(string tableName) =>
                DoesTableExist(DataUniversal.BuildConnectionString(), tableName);

            public static bool DoesTableExist(string connectionString, string tableName)
            {
                long cnt = 0;
                DataUniversal.ExecuteSQL(connectionString, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@tableName;",
                    new Dictionary<string, object> { { "@tableName", tableName } },
                    cmd => cnt = (long)cmd.ExecuteScalar());
                return cnt > 0;
            }

            public static void CreateTable(string tableName, Func<string> createTableQuery) =>
                CreateTable(DataUniversal.BuildConnectionString(), tableName, createTableQuery);

            public static void CreateTable(string connectionString, string tableName, Func<string> createTableQuery)
            {
                if (!DoesTableExist(connectionString, tableName))
                    DataUniversal.ExecuteNonQuery(connectionString, createTableQuery());
            }

            public static void DropTable(string tableName) =>
                DropTable(DataUniversal.BuildConnectionString(), tableName);

            public static void DropTable(string connectionString, string tableName) =>
                DataUniversal.ExecuteNonQuery(connectionString, string.Format("DROP TABLE IF EXISTS {0}", tableName));
        }

        public static class DataSave
        {
            public static int SaveSingleRow(string tableName, Dictionary<string, object> parameters)
            {
                string cols = string.Join(", ", parameters.Keys);
                string vals = string.Join(", ", parameters.Keys.Select(k => $"@{k}"));
                int rows = 0;
                DataUniversal.ExecuteSQL(string.Format("INSERT INTO {0} ({1}) VALUES ({2})", tableName, cols, vals), parameters,
                    cmd => rows = cmd.ExecuteNonQuery());
                if (rows == 0) throw new Exception($"插入数据失败: 表 {tableName}");
                return rows;
            }

            public static int DeleteRow(string tableName, string rowName, string valueToMatch)
            {
                int rows = 0;
                DataUniversal.ExecuteSQL(string.Format("DELETE FROM [{0}] WHERE [{1}] = @value", tableName, rowName),
                    new Dictionary<string, object> { { "@value", valueToMatch } },
                    cmd => rows = cmd.ExecuteNonQuery());
                if (rows == 0) throw new Exception($"删除数据失败: 表 {tableName}, 条件 {rowName}={valueToMatch}");
                return rows;
            }
        }

        public static class DataRead
        {
            public static List<string> GetTableColumns(string tableName)
            {
                var cols = new List<string>();
                DataUniversal.ExecuteQueryCore(string.Format("PRAGMA table_info({0})", tableName),
                    r => cols.Add(r.GetString(1)));
                return cols;
            }

            public static List<string> GetRowValue(string tableName, string rowName, string valueToMatch)
            {
                var res = new List<string>();
                DataUniversal.ExecuteQueryCore(string.Format("SELECT * FROM [{0}] WHERE [{1}] = @value LIMIT 1", tableName, rowName), r =>
                {
                    for (int i = 0; i < r.FieldCount; i++) res.Add(r.GetValue(i).ToString());
                });
                if (res.Count == 0) throw new Exception($"未找到匹配的数据: 表 {tableName}, 条件 {rowName}={valueToMatch}");
                return res;
            }

            public static List<string> SQLiteGetTableNames()
            {
                var names = new List<string>();
                DataUniversal.ExecuteQueryCore("SELECT name FROM sqlite_master WHERE type='table';",
                    r => names.Add(r.GetString(0)));
                return names;
            }
        }

        public static class AutoStorage
        {
            private const string IdColumn = "Id";
            private const string RowNameColumn = "RowName";

            private static void EnsureTableInternal(string tableName, IEnumerable<string> cols = null)
            {
                if (DataCreate.DoesTableExist(tableName)) return;
                var defs = new List<string> { "[Id] INTEGER PRIMARY KEY AUTOINCREMENT" };
                if (cols != null)
                    defs.AddRange(cols.Where(c => !string.Equals(c, IdColumn, StringComparison.OrdinalIgnoreCase))
                                      .Select(c => $"[{c}] TEXT"));
                DataUniversal.ExecuteNonQuery($"CREATE TABLE [{tableName}] ({string.Join(", ", defs)})");
            }

            public static void EnsureTable(string tableName) => EnsureTableInternal(tableName, new[] { "Content" });

            public static int SaveData(string tableName, Dictionary<string, object> data)
            {
                if (data == null) throw new ArgumentNullException(nameof(data));

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                data[RowNameColumn] = timestamp;

                var dataCopy = new Dictionary<string, object>(data);
                dataCopy.Remove(IdColumn);
                if (dataCopy.Count == 0) return 0;

                EnsureTableInternal(tableName, dataCopy.Keys);

                var cols = dataCopy.Keys.ToList();
                var parameters = dataCopy.ToDictionary(kv => "@" + kv.Key, kv => kv.Value ?? DBNull.Value);
                string sql = $"INSERT INTO [{tableName}] ({string.Join(", ", cols.Select(c => $"[{c}]"))}) " +
                             $"VALUES ({string.Join(", ", cols.Select(c => "@" + c))})";
                int rows = 0;
                DataUniversal.ExecuteSQL(sql, parameters, cmd => rows = cmd.ExecuteNonQuery());
                return rows;
            }

            public static List<Dictionary<string, object>> LoadAllData(string tableName)
            {
                var result = new List<Dictionary<string, object>>();
                DataUniversal.ExecuteQueryCore($"SELECT * FROM [{tableName}]", r =>
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.GetValue(i);
                    result.Add(row);
                });
                return result;
            }

            public static void SaveString(string tableName, string content) =>
                SaveData(tableName, new Dictionary<string, object> { { "Content", content } });

            public static List<string> LoadAllStrings(string tableName)
            {
                var result = new List<string>();
                var allRows = LoadAllData(tableName);
                if (allRows.Count == 0) return result;

                var contentColumns = DataRead.GetTableColumns(tableName)
                                             .Where(col => col.StartsWith("Content", StringComparison.OrdinalIgnoreCase))
                                             .ToList();

                foreach (var row in allRows)
                {
                    foreach (var col in contentColumns)
                    {
                        if (row.TryGetValue(col, out var val) && val != null && val != DBNull.Value)
                            result.Add(val.ToString());
                    }
                }
                return result;
            }
        }
    }
}