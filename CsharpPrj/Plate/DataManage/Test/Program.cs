#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Core.Models;
using DataManage.Usage;

namespace UniversalWorkflowFramework.DataManage
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== 验证之前存储的数据 ===");
            
            string storagePath = "./DataBase/SQLite";
            
            var manager = new DataStorageManager(storagePath, enableLogging: true);
            
            try
            {
                Console.WriteLine("\n[1] 验证统一存储管理器数据");
                var unifiedManager = await manager.GetOrCreateUnifiedManager();
                
                string testKey = "test:key1";
                var value = await manager.LoadAsync<string>(testKey);
                if (value != null)
                {
                    Console.WriteLine($"✓ 成功读取数据 [{testKey}]: {value}");
                }
                else
                {
                    Console.WriteLine($"✗ 未找到数据 [{testKey}]");
                }
                
                bool exists = await manager.ExistsAsync(testKey);
                Console.WriteLine($"数据 [{testKey}] 是否存在: {exists}");
                
                var keys = await manager.GetKeysAsync();
                Console.WriteLine($"存储中的所有键 ({keys.Count()} 个):");
                foreach (var key in keys)
                {
                    Console.WriteLine($"  - {key}");
                }
                
                Console.WriteLine("\n[2] 验证工作流存储");
                string workflowId = "workflow:test001";
                var loadedWorkflow = await manager.LoadWorkflowAsync(workflowId);
                if (loadedWorkflow != null)
                {
                    Console.WriteLine($"✓ 成功读取工作流 [{workflowId}]: {loadedWorkflow}");
                }
                else
                {
                    Console.WriteLine($"✗ 未找到工作流 [{workflowId}]");
                }
                
                bool workflowExists = await manager.WorkflowExistsAsync(workflowId);
                Console.WriteLine($"工作流 [{workflowId}] 是否存在: {workflowExists}");
                
                Console.WriteLine("\n[3] 验证图存储数据");
                var resourceId = new ResourceIdentifier 
                { 
                    ResourceType = "document", 
                    Id = "doc001" 
                };
                
                bool resourceExists = await manager.ResourceExistsAsync(resourceId);
                Console.WriteLine($"资源 [{resourceId.ResourceType}/{resourceId.Id}] 是否存在: {resourceExists}");
                
                if (resourceExists)
                {
                    var resource = await manager.GetResourceAsync(resourceId);
                    if (resource != null)
                    {
                        Console.WriteLine($"✓ 成功读取资源: {resource.Identifier.ResourceType}/{resource.Identifier.Id}");
                        Console.WriteLine("  元数据:");
                        foreach (var kvp in resource.Metadata)
                        {
                            Console.WriteLine($"    {kvp.Key}: {kvp.Value}");
                        }
                    }
                }
                
                Console.WriteLine("\n[4] 统计信息");
                var countByType = await manager.CountByTypeGroupAsync();
                Console.WriteLine("按类型分组计数:");
                foreach (var kvp in countByType)
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
                
                Console.WriteLine("\n[5] 获取文档类型资源列表");
                var documents = await manager.GetResourcesByTypeAsync("document");
                Console.WriteLine($"文档类型资源数量: {documents.Count}");
                foreach (var doc in documents)
                {
                    Console.WriteLine($"  - {doc.Identifier.Id}: {doc.Metadata.GetValueOrDefault("title", "无标题")}");
                }
                
                Console.WriteLine("\n=== 数据验证完成 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
