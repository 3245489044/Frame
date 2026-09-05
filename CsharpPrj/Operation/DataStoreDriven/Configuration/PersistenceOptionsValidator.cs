using System;
using System.IO;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Configuration;

namespace Operation.DataStoreDriven.Configuration;

/// <summary>
/// 配置驱动后校验（组合根 fail-fast）：Provider 合法、连接串/路径非空、路径可写。
/// </summary>
public static class PersistenceOptionsValidator
{
    public static void Validate(PersistenceOptions options, string? configRoot = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Enum.IsDefined(typeof(PersistenceProvider), options.Provider))
            throw new InvalidOperationException($"未识别的持久化后端: {options.Provider}");

        if (options.Provider == PersistenceProvider.Sqlite || options.Provider == PersistenceProvider.LiteDB)
        {
            if (string.IsNullOrWhiteSpace(options.TablePrefix))
                throw new InvalidOperationException("[Persistence] TablePrefix 未配置（默认由 AgentConfig 统一提供）。");

            if (string.IsNullOrWhiteSpace(options.ConnectionString) && string.IsNullOrWhiteSpace(options.DatabasePath))
                throw new InvalidOperationException($"[Persistence] {options.Provider} 后端必须配置 ConnectionString 或 DatabasePath。");

            var dbPath = PersistenceHelper.ResolveDatabasePath(options.DatabasePath, configRoot);
            if (!string.IsNullOrWhiteSpace(dbPath))
                EnsureDirectoryWritable(dbPath);
        }

        if (options.Provider == PersistenceProvider.Local && string.IsNullOrWhiteSpace(options.DatabasePath))
            throw new InvalidOperationException("[Persistence] Local 后端必须配置 DatabasePath（默认由 AgentConfig 统一提供）。");
    }

    private static void EnsureDirectoryWritable(string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (string.IsNullOrWhiteSpace(dir))
            return;
        try
        {
            Directory.CreateDirectory(dir);
            // 真实写探测：创建并删除临时文件，确保目录确实可写（名实相符，而非仅 CreateDirectory）。
            var probe = Path.Combine(dir, $".write_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[Persistence] 数据库目录不可写: {dir} ({ex.Message})", ex);
        }
    }
}
