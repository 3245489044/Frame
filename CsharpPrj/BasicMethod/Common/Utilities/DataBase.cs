namespace Common.GraphMindStudio.Data
{
    using System;
    using System.Collections.Generic;
    using System.Data.SQLite;

    public static class DataBase
    {
        static public class DataUniversal
        {
            private const string DataBaseFolder = "DataBase";
            private const string MainDataBaseFileName = "MainDataBase.db";
            private const string ConnectionStringPrefix = "Data Source=";
            private const string ConnectionStringSuffix = ";Version=3;";
            private const string SqlExecutionFailedMessage = "SQL执行失败: {0}";
            private const string SqlQueryExecutionFailedMessage = "SQL查询执行失败: {0}\nSQL: {1}";
            private const string PragmaTableInfoTemplate = "PRAGMA table_info({0});";

            static public string DataBasePath = string.Empty;

            static public SQLiteConnection SetConnect()
            {
                var conn = new SQLiteConnection($"{ConnectionStringPrefix}{DataBasePath}{ConnectionStringSuffix}");
                try { conn.Open(); }
                catch (Exception ex) { throw new Exception($"数据库连接失败: {ex.Message}", ex); }
                return conn;
            }

            public static void ExecuteNonQuery(string sql, Dictionary<string, object> parameters = null) =>
                ExecuteSQL(sql, parameters, cmd => cmd.ExecuteNonQuery());

            public static void QueryRow(string query, object valueToMatch, Action<SQLiteDataReader> handleRow) =>
                ExecuteSQL(query, new Dictionary<string, object> { { "@value", valueToMatch } }, cmd =>
                {
                    using var r = cmd.ExecuteReader();
                    if (r.Read()) handleRow(r);
                });

            private static void ExecuteSQL(string sql, Dictionary<string, object> parameters, Action<SQLiteCommand> action)
            {
                using var conn = SetConnect();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }
                }

                try
                {
                    action(cmd);
                }
                catch (Exception ex)
                {
                    throw new Exception(string.Format(SqlExecutionFailedMessage, ex.Message), ex);
                }
            }
        }
    }
}
