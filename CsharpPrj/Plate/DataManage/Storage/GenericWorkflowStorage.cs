#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace UniversalWorkflowFramework.Storage
{
    public class GenericWorkflowStorage<TWorkflow> : global::DataManage.Abstractions.IWorkflowStorage<TWorkflow>
    {
        private readonly string _storagePath;
        private readonly ILogger<GenericWorkflowStorage<TWorkflow>>? _logger;

        public GenericWorkflowStorage(string storagePath, ILogger<GenericWorkflowStorage<TWorkflow>>? logger = null)
        {
            _storagePath = storagePath;
            _logger = logger;
            Directory.CreateDirectory(_storagePath);
        }

        public async Task InitializeAsync()
        {
            await Task.CompletedTask;
        }

        public async Task StoreAsync(string workflowId, TWorkflow workflowData)
        {
            try
            {
                var filePath = GetFilePath(workflowId);
                var json = SerializeWorkflow(workflowData);
                await File.WriteAllTextAsync(filePath, json);
                _logger?.LogInformation("存储工作流成功: {WorkflowId}", workflowId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "存储工作流失败: {WorkflowId}", workflowId);
                throw;
            }
        }

        public async Task<TWorkflow?> RetrieveAsync(string workflowId)
        {
            try
            {
                var filePath = GetFilePath(workflowId);
                if (!File.Exists(filePath))
                {
                    _logger?.LogInformation("检索工作流成功: {WorkflowId}", workflowId);
                    return default;
                }

                var json = await File.ReadAllTextAsync(filePath);
                var workflow = DeserializeWorkflow(json);
                _logger?.LogInformation("检索工作流成功: {WorkflowId}", workflowId);
                return workflow;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "检索工作流失败: {WorkflowId}", workflowId);
                throw;
            }
        }

        public async Task DeleteAsync(string workflowId)
        {
            try
            {
                var filePath = GetFilePath(workflowId);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                _logger?.LogInformation("删除工作流成功: {WorkflowId}", workflowId);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "删除工作流失败: {WorkflowId}", workflowId);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string workflowId)
        {
            try
            {
                var filePath = GetFilePath(workflowId);
                var exists = File.Exists(filePath);
                _logger?.LogInformation("检查工作流存在性: {WorkflowId} - {Exists}", workflowId, exists);
                await Task.CompletedTask;
                return exists;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "检查工作流存在性失败: {WorkflowId}", workflowId);
                return false;
            }
        }

        public async Task<List<string>> GetAllWorkflowIdsAsync()
        {
            try
            {
                var workflowIds = new List<string>();
                if (Directory.Exists(_storagePath))
                {
                    var files = Directory.GetFiles(_storagePath, "*.json");
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        workflowIds.Add(fileName);
                    }
                }
                _logger?.LogInformation("获取工作流ID列表成功，共 {Count} 个工作流", workflowIds.Count);
                await Task.CompletedTask;
                return workflowIds;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "获取工作流ID列表失败");
                return new List<string>();
            }
        }

        public async Task ClearAllWorkflowsAsync()
        {
            try
            {
                if (Directory.Exists(_storagePath))
                {
                    var files = Directory.GetFiles(_storagePath, "*.json");
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }
                _logger?.LogInformation("清空所有工作流成功");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "清空工作流失败");
                throw;
            }
        }

        private string GetFilePath(string workflowId)
        {
            return Path.Combine(_storagePath, $"{workflowId}.json");
        }

        private string SerializeWorkflow(TWorkflow workflow)
        {
            return JsonSerializer.Serialize(workflow);
        }

        private TWorkflow? DeserializeWorkflow(string json)
        {
            return JsonSerializer.Deserialize<TWorkflow>(json);
        }
    }
}
