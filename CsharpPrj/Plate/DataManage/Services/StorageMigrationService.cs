#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataManage.Abstractions;
using Microsoft.Extensions.Logging;

namespace DataManage.Services
{
    public class StorageMigrationService
    {
        private readonly ILogger<StorageMigrationService>? _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public StorageMigrationService(ILogger<StorageMigrationService>? logger = null)
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
        }

        public async Task MigrateFromLegacyToDataMethodAsync(
            IDataStorage legacyStorage,
            StorageProvider newStorage,
            string targetContainer = "Workflows")
        {
            try
            {
                _logger?.LogInformation("开始数据迁移：从旧存储迁移到 DataMethod 存储");

                var keys = await legacyStorage.GetKeysAsync();
                var keyList = keys.ToList();

                _logger?.LogInformation("发现 {Count} 条待迁移的数据", keyList.Count);

                int migratedCount = 0;
                int failedCount = 0;

                foreach (var key in keyList)
                {
                    try
                    {
                        var workflow = await legacyStorage.LoadAsync<WorkflowDefinition>(key);
                        if (workflow == null)
                        {
                            _logger?.LogWarning("跳过空数据: {Key}", key);
                            continue;
                        }

                        var data = ConvertToDataMethodFormat(workflow, key);

                        await newStorage.SaveAsync(targetContainer, data);
                        migratedCount++;

                        _logger?.LogDebug("迁移成功: {Key}", key);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "迁移失败: {Key}", key);
                        failedCount++;
                    }
                }

                _logger?.LogInformation("数据迁移完成：成功 {Migrated} 条，失败 {Failed} 条", migratedCount, failedCount);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "数据迁移过程中发生错误");
                throw;
            }
        }

        public async Task MigrateFromFileSystemToDataMethodAsync(
            string sourceDirectory,
            StorageProvider newStorage,
            string targetContainer = "Workflows")
        {
            try
            {
                _logger?.LogInformation("开始数据迁移：从文件系统迁移到 DataMethod 存储");

                if (!System.IO.Directory.Exists(sourceDirectory))
                {
                    _logger?.LogWarning("源目录不存在: {Directory}", sourceDirectory);
                    return;
                }

                var jsonFiles = System.IO.Directory.GetFiles(sourceDirectory, "*.json");
                _logger?.LogInformation("发现 {Count} 个待迁移的 JSON 文件", jsonFiles.Length);

                int migratedCount = 0;
                int failedCount = 0;

                foreach (var filePath in jsonFiles)
                {
                    try
                    {
                        var key = System.IO.Path.GetFileNameWithoutExtension(filePath);
                        var jsonContent = await System.IO.File.ReadAllTextAsync(filePath);
                        var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(jsonContent, _jsonOptions);

                        if (workflow == null)
                        {
                            _logger?.LogWarning("跳过无效的 JSON 文件: {File}", filePath);
                            continue;
                        }

                        var data = ConvertToDataMethodFormat(workflow, key);
                        await newStorage.SaveAsync(targetContainer, data);
                        migratedCount++;

                        _logger?.LogDebug("迁移成功: {File}", filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "迁移失败: {File}", filePath);
                        failedCount++;
                    }
                }

                _logger?.LogInformation("文件系统迁移完成：成功 {Migrated} 个，失败 {Failed} 个", migratedCount, failedCount);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "文件系统迁移过程中发生错误");
                throw;
            }
        }

        public async Task<bool> VerifyMigrationAsync(
            IDataStorage legacyStorage,
            StorageProvider newStorage,
            string targetContainer = "Workflows")
        {
            try
            {
                _logger?.LogInformation("开始验证迁移结果");

                var legacyKeys = (await legacyStorage.GetKeysAsync()).ToHashSet();
                var newData = await newStorage.LoadAllAsync(targetContainer);
                var newKeys = newData.Select(d => d["_key"]?.ToString()).Where(k => !string.IsNullOrEmpty(k)).ToHashSet();

                var missingInNew = legacyKeys.Except(newKeys).ToList();
                var extraInNew = newKeys.Except(legacyKeys).ToList();

                if (missingInNew.Any())
                {
                    _logger?.LogWarning("发现 {Count} 条数据在新存储中缺失", missingInNew.Count);
                    foreach (var key in missingInNew)
                    {
                        _logger?.LogDebug("缺失数据: {Key}", key);
                    }
                }

                if (extraInNew.Any())
                {
                    _logger?.LogWarning("发现 {Count} 条额外数据", extraInNew.Count);
                }

                var allMatch = !missingInNew.Any() && !extraInNew.Any();
                _logger?.LogInformation("迁移验证{Status}", allMatch ? "通过" : "未通过");

                return allMatch;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "迁移验证过程中发生错误");
                throw;
            }
        }

        private Dictionary<string, object> ConvertToDataMethodFormat(WorkflowDefinition workflow, string originalKey)
        {
            var data = new Dictionary<string, object>
            {
                ["_key"] = originalKey,
                ["Id"] = workflow.Id,
                ["Name"] = workflow.Name,
                ["Description"] = workflow.Description,
                ["Version"] = workflow.Version,
                ["CreatedAt"] = workflow.CreatedAt.ToString("O"),
                ["UpdatedAt"] = workflow.UpdatedAt.ToString("O"),
                ["IsActive"] = workflow.IsActive,
                ["Author"] = workflow.Metadata?.Author ?? string.Empty,
                ["Category"] = workflow.Metadata?.Category ?? string.Empty,
                ["JsonContent"] = JsonSerializer.Serialize(workflow, _jsonOptions)
            };

            if (workflow.Metadata?.CustomProperties != null)
            {
                foreach (var prop in workflow.Metadata.CustomProperties)
                {
                    data[$"Custom_{prop.Key}"] = prop.Value ?? string.Empty;
                }
            }

            return data;
        }

        private class WorkflowDefinition
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool IsActive { get; set; }
            public WorkflowMetadata? Metadata { get; set; }
        }

        private class WorkflowMetadata
        {
            public string? Author { get; set; }
            public string? Category { get; set; }
            public Dictionary<string, string>? CustomProperties { get; set; }
        }
    }
}
