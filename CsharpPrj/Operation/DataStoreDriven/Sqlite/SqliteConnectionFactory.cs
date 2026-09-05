using System;
using System.IO;
using System.Data.SQLite;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>Sqlite 连接工厂：由 <see cref="PersistenceOptions"/> 构造连接串。显式 ConnectionString 优先；否则由 DatabasePath 构造。</summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly string? _databasePath;

    public SqliteConnectionFactory(PersistenceOptions options, string? basePath = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connectionString = PersistenceHelper.BuildConnectionString(PersistenceProvider.Sqlite, options, basePath);
        if (string.IsNullOrWhiteSpace(options.ConnectionString) && !string.IsNullOrWhiteSpace(options.DatabasePath))
            _databasePath = PersistenceHelper.ResolveDatabasePath(options.DatabasePath, basePath);
    }

    public string ConnectionString => _connectionString;

    /// <summary>解析出的数据库文件路径（仅在提供 DatabasePath 时有值）。</summary>
    public string? DatabasePath => _databasePath;

    /// <summary>确保数据库目录/文件存在（SQLite 打开连接执行语句时自动建库，但目录须先存在）。</summary>
    public void EnsureFileCreated()
    {
        if (string.IsNullOrWhiteSpace(_databasePath)) return;
        PersistenceHelper.EnsureDirectoryFor(_databasePath);
        if (!File.Exists(_databasePath)) SQLiteConnection.CreateFile(_databasePath);
    }
}
