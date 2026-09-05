using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.ActionMapping.Mappers;
using UniversalWorkflowFramework.ActionMapping.Models;
using UniversalWorkflowFramework.BranchManagement.Managers;
using UniversalWorkflowFramework.BranchManagement.Models;
using UniversalWorkflowFramework.Reference.Models;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.EventDriven;
using UniversalWorkflowFramework.Reference;
using UniversalWorkflowFramework.ReferenceManagement.Managers;
using UniversalWorkflowFramework.Core.Managers;
using UniversalWorkflowFramework.Storage;
using UniversalWorkflowFramework.Storage.GraphDatabase;
using UniversalWorkflowFramework.Storage.Local;
using UniversalWorkflowFramework.WorkflowExecution.Executors;

namespace BasicMethod.Tests.UniversalReference
{
    [TestClass]
    [TestGroup("UniversalReference")]
    public class CompleteUsageExample : TestBase
    {
        private ILogger _logger = null!;
        private static readonly string TestDataDirectory = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory,
            "TestData");
        private static readonly string TestDatabaseName = Path.Combine(TestDataDirectory, "workflow-test.db");

        [TestInitialize]
        public void TestInitialize()
        {
            var logFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            _logger = logFactory.CreateLogger("UniversalReference.Tests.CompleteUsageExample");
        }

        [TestMethod]
        public async Task RunAllExamplesAsync()
        {
            _logger.LogInformation("===== 通用工作流框架 - 完整使用示例 =====");

            await SetupTestEnvironment();

            await ActionMapping_ShouldMapUIActionsToAIActions();

            await BranchManagement_ShouldCreateAndManageBranches();

            await WorkflowExecution_ShouldExecuteActionsAndBranches();

            await Storage_ShouldStoreAndRetrieveData();

            await ReferenceManagement_ShouldManageResourceReferences();

            await EventDriven_ShouldTriggerEventsOnResourceChanges();

            await LiteDBPersistence_ShouldPersistNodesAndEdges();

            await UniversalReferenceManager_ShouldManageResourcesAndReferences();

            await GraphDatabaseStorage_ShouldStoreAndRetrieveGraphData();

            await CleanupTestEnvironment();

            _logger.LogInformation("===== 所有示例执行完成 =====");
        }

        #region 0. 测试环境准备
                private async Task SetupTestEnvironment()
        {
            _logger.LogInformation("\n--- 0. 准备测试环境 ---");

            try
            {
                if (!System.IO.Directory.Exists(TestDataDirectory))
                {
                    System.IO.Directory.CreateDirectory(TestDataDirectory);
                    _logger.LogInformation($"创建测试目录: {TestDataDirectory}");
                }

                if (System.IO.File.Exists(TestDatabaseName))
                {
                    System.IO.File.Delete(TestDatabaseName);
                    _logger.LogInformation("删除旧测试数据库");
                }

                var graphStorage = GraphDatabaseStorageFactory.Create(_logger);
                await graphStorage.InitializeAsync();
                _logger.LogInformation("初始化图数据库存储");

                _logger.LogInformation("测试环境准备完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "准备测试环境失败");
                throw;
            }
        }
        #endregion

        #region 1. 操作映射示例
                [TestMethod]
        public async Task ActionMapping_ShouldMapUIActionsToAIActions()
        {
            _logger.LogInformation("\n--- 1. 操作映射示例 ---");

            var actionMapper = ActionMapperFactory.Create<UIAction, AIAction>();
            await actionMapper.InitializeAsync();

            await actionMapper.AddMappingAsync("click", "ai_click", new Dictionary<string, string>
            {
                { "buttonId", "elementId" },
                { "text", "content" }
            });

            await actionMapper.AddMappingAsync("input", "ai_input", new Dictionary<string, string>
            {
                { "fieldId", "targetElement" }
            });

            await actionMapper.AddMappingAsync("submit", "ai_submit", new Dictionary<string, string>
            {
                { "formId", "targetForm" },
                { "data", "payload" }
            });

            var uiAction = new UIAction
            {
                ActionType = "click",
                Parameters = new Dictionary<string, object>
                {
                    { "buttonId", "submit-btn" },
                    { "text", "提交" }
                }
            };

            var aiAction = await actionMapper.MapAsync(uiAction);
            _logger.LogInformation($"映射结果: {uiAction.ActionType} -> {aiAction.ActionType}");
            _logger.LogInformation($"参数映射: buttonId={uiAction.Parameters["buttonId"]} -> elementId={aiAction.Parameters["elementId"]}");

            Assert.AreEqual("ai_click", aiAction.ActionType, "click 应映射为 ai_click");
            Assert.AreEqual("submit-btn", aiAction.Parameters["elementId"], "buttonId 应映射为 elementId");

            var uiInputAction = new UIAction
            {
                ActionType = "input",
                Parameters = new Dictionary<string, object>
                {
                    { "fieldId", "username" },
                    { "value", "testuser" }
                }
            };
            var aiInputAction = await actionMapper.MapAsync(uiInputAction);
            _logger.LogInformation($"映射结果: {uiInputAction.ActionType} -> {aiInputAction.ActionType}");
            Assert.AreEqual("ai_input", aiInputAction.ActionType, "input 应映射为 ai_input");

            var allMappings = await actionMapper.GetAllMappingsAsync();
            _logger.LogInformation($"当前映射数量: {allMappings.Count}");
            Assert.AreEqual(3, allMappings.Count, "应有3个映射");

            await actionMapper.RemoveMappingAsync("click");

            allMappings = await actionMapper.GetAllMappingsAsync();
            _logger.LogInformation($"删除后映射数量: {allMappings.Count}");
            Assert.AreEqual(2, allMappings.Count, "删除后应有2个映射");

            _logger.LogInformation("操作映射示例完成");
        }
        #endregion

        #region 2. 分支管理示例
                [TestMethod]
        public async Task BranchManagement_ShouldCreateAndManageBranches()
        {
            _logger.LogInformation("\n--- 2. 分支管理示例 ---");

            var branchStorage = LocalStorageFactory.Create<BranchGraph>("BranchStorage");

            var branchManager = BranchManagerFactory.Create<BranchGraph>(branchStorage);
            await branchManager.InitializeAsync();

            var userRegistrationBranch = new BranchGraph
            {
                Name = "用户注册流程",
                Description = "用户注册的完整工作流分支",
                Nodes = new List<BranchNode>
                {
                    new BranchNode { NodeId = "start", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.Start, Name = "开始" },
                    new BranchNode { NodeId = "validate", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.Action, Name = "验证输入" },
                    new BranchNode { NodeId = "decision", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.Decision, Name = "验证结果" },
                    new BranchNode { NodeId = "save", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.Action, Name = "保存用户" },
                    new BranchNode { NodeId = "notify", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.Action, Name = "发送通知" },
                    new BranchNode { NodeId = "end", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.End, Name = "结束" }
                },
                Edges = new List<BranchEdge>
                {
                    new BranchEdge { EdgeId = "e1", SourceNodeId = "start", TargetNodeId = "validate", Type = UniversalWorkflowFramework.BranchManagement.Models.EdgeType.Normal },
                    new BranchEdge { EdgeId = "e2", SourceNodeId = "validate", TargetNodeId = "decision", Type = UniversalWorkflowFramework.BranchManagement.Models.EdgeType.Normal },
                    new BranchEdge { EdgeId = "e3", SourceNodeId = "decision", TargetNodeId = "save", Type = UniversalWorkflowFramework.BranchManagement.Models.EdgeType.Conditional, Data = { { "condition", "valid" } } },
                    new BranchEdge { EdgeId = "e4", SourceNodeId = "save", TargetNodeId = "notify", Type = UniversalWorkflowFramework.BranchManagement.Models.EdgeType.Normal },
                    new BranchEdge { EdgeId = "e5", SourceNodeId = "notify", TargetNodeId = "end", Type = UniversalWorkflowFramework.BranchManagement.Models.EdgeType.Normal }
                }
            };

            var branchId1 = await branchManager.RegisterBranchAsync("user-registration", userRegistrationBranch);
            _logger.LogInformation($"注册分支成功: {branchId1}");

            var orderProcessingBranch = new BranchGraph
            {
                Name = "订单处理流程",
                Description = "订单处理的完整工作流分支",
                Nodes = new List<BranchNode>
                {
                    new BranchNode { NodeId = "order_start", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.Start, Name = "订单开始" },
                    new BranchNode { NodeId = "check_inventory", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.Action, Name = "检查库存" },
                    new BranchNode { NodeId = "inventory_decision", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.Decision, Name = "库存判断" },
                    new BranchNode { NodeId = "process_payment", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.Action, Name = "处理支付" },
                    new BranchNode { NodeId = "ship_order", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.Action, Name = "发货" },
                    new BranchNode { NodeId = "order_end", Type = UniversalWorkflowFramework.BranchManagement.Models.NodeType.End, Name = "订单结束" }
                },
                Edges = new List<BranchEdge>
                {
                    new BranchEdge { EdgeId = "oe1", SourceNodeId = "order_start", TargetNodeId = "check_inventory", Type = UniversalWorkflowFramework.BranchManagement.Models.EdgeType.Normal },
                    new BranchEdge { EdgeId = "oe2", SourceNodeId = "check_inventory", TargetNodeId = "inventory_decision", Type = UniversalWorkflowFramework.BranchManagement.Models.EdgeType.Normal },
                    new BranchEdge { EdgeId = "oe3", SourceNodeId = "inventory_decision", TargetNodeId = "process_payment", Type = UniversalWorkflowFramework.BranchManagement.Models.EdgeType.Conditional, Data = { { "condition", "in_stock" } } },
                    new BranchEdge { EdgeId = "oe4", SourceNodeId = "process_payment", TargetNodeId = "ship_order", Type = UniversalWorkflowFramework.BranchManagement.Models.EdgeType.Normal },
                    new BranchEdge { EdgeId = "oe5", SourceNodeId = "ship_order", TargetNodeId = "order_end", Type = UniversalWorkflowFramework.BranchManagement.Models.EdgeType.Normal }
                }
            };

            var branchId2 = await branchManager.RegisterBranchAsync("order-processing", orderProcessingBranch);
            _logger.LogInformation($"注册分支成功: {branchId2}");

            var retrievedBranch = await branchManager.GetBranchAsync(branchId1);
            _logger.LogInformation($"获取分支: {retrievedBranch?.Name}");
            Assert.IsNotNull(retrievedBranch, "应能获取到注册的分支");
            Assert.AreEqual("用户注册流程", retrievedBranch.Name, "分支名称应匹配");

            var validationResult = await branchManager.ValidateBranchAsync(branchId1);
            _logger.LogInformation($"分支验证: {validationResult.IsValid} - {validationResult.Message}");

            var exists = await branchManager.BranchExistsAsync(branchId1);
            _logger.LogInformation($"分支存在: {exists}");
            Assert.IsTrue(exists, "分支应存在");

            var allBranchIds = await branchManager.GetAllBranchIdsAsync();
            _logger.LogInformation($"分支总数: {allBranchIds.Count}");
            Assert.AreEqual(2, allBranchIds.Count, "应有2个分支");

            await branchManager.RemoveBranchAsync(branchId1);
            await branchManager.RemoveBranchAsync(branchId2);

            allBranchIds = await branchManager.GetAllBranchIdsAsync();
            _logger.LogInformation($"删除后分支数量: {allBranchIds.Count}");
            Assert.AreEqual(0, allBranchIds.Count, "删除后应有0个分支");

            _logger.LogInformation("分支管理示例完成");
        }
        #endregion

        #region 3. 工作流执行示例
                [TestMethod]
        public async Task WorkflowExecution_ShouldExecuteActionsAndBranches()
        {
            _logger.LogInformation("\n--- 3. 工作流执行示例 ---");

            var workflowExecutor = WorkflowExecutorFactory.Create();
            await workflowExecutor.InitializeAsync();

            var uiAction = new UIAction
            {
                ActionType = "submit",
                Parameters = new Dictionary<string, object>
                {
                    { "formId", "login-form" },
                    { "username", "testuser" }
                }
            };

            var actionResult = await workflowExecutor.ExecuteAsync(uiAction, isUIAction: true);
            _logger.LogInformation($"执行操作结果: {actionResult.Status}");
            Assert.IsNotNull(actionResult, "执行结果不应为空");

            var uiAction2 = new UIAction
            {
                ActionType = "click",
                Parameters = new Dictionary<string, object>
                {
                    { "buttonId", "search-btn" },
                    { "query", "test" }
                }
            };

            var actionResult2 = await workflowExecutor.ExecuteAsync(uiAction2, isUIAction: true);
            _logger.LogInformation($"执行操作结果: {actionResult2.Status}");

            var aiAction = new AIAction
            {
                ActionType = "ai_analyze",
                Parameters = new Dictionary<string, object>
                {
                    { "input", "analyze this" },
                    { "model", "gpt-4" }
                }
            };

            var aiResult = await workflowExecutor.ExecuteAsync(aiAction, isUIAction: false);
            _logger.LogInformation($"执行AI操作结果: {aiResult.Status}");

            var branchResult = await workflowExecutor.ExecuteBranchAsync("checkout-flow", new Dictionary<string, object>
            {
                { "userId", "u123" },
                { "amount", 99.99 }
            });
            _logger.LogInformation($"执行分支结果: {branchResult.Status}");

            var branchResult2 = await workflowExecutor.ExecuteBranchAsync("payment-flow", new Dictionary<string, object>
            {
                { "orderId", "order-001" },
                { "method", "credit_card" }
            });
            _logger.LogInformation($"执行分支结果: {branchResult2.Status}");

            var validationResult = await workflowExecutor.ValidateExecutionAsync("test-workflow", actionResult, actionResult2);
            _logger.LogInformation($"执行验证(成功): {validationResult.IsValid} - {validationResult.Message}");

            var failedResult = new ExecutionResult { Status = ExecutionStatus.Failed, Error = "测试错误" };
            var failedValidation = await workflowExecutor.ValidateExecutionAsync("failed-workflow", actionResult, failedResult);
            _logger.LogInformation($"执行验证(失败): {failedValidation.IsValid} - {failedValidation.Message}");

            _logger.LogInformation("工作流执行示例完成");
        }
        #endregion

        #region 4. 存储模块示例
                [TestMethod]
        public async Task Storage_ShouldStoreAndRetrieveData()
        {
            _logger.LogInformation("\n--- 4. 存储模块示例 ---");

            var storage = LocalStorageFactory.Create<Dictionary<string, object>>("ExampleData");

            var data = new Dictionary<string, object>
            {
                { "name", "Test Workflow" },
                { "version", "1.0.0" },
                { "createdAt", DateTime.UtcNow },
                { "active", true }
            };

            await storage.StoreAsync("workflow-001", data);
            _logger.LogInformation("存储数据成功");

            var data2 = new Dictionary<string, object>
            {
                { "name", "Another Workflow" },
                { "version", "2.0.0" },
                { "createdAt", DateTime.UtcNow },
                { "active", false }
            };

            await storage.StoreAsync("workflow-002", data2);
            _logger.LogInformation("存储第二个数据成功");

            var retrieved = await storage.RetrieveAsync("workflow-001");
            Assert.IsNotNull(retrieved, "应能检索到存储的数据");
            if (retrieved != null)
            {
                _logger.LogInformation($"检索数据: name={retrieved["name"]}, version={retrieved["version"]}");
                Assert.AreEqual("Test Workflow", retrieved["name"], "数据名称应匹配");
            }

            var exists = await storage.ExistsAsync("workflow-001");
            _logger.LogInformation($"数据存在: {exists}");
            Assert.IsTrue(exists, "数据应存在");

            var notExists = await storage.ExistsAsync("workflow-999");
            _logger.LogInformation($"不存在的数据: {notExists}");
            Assert.IsFalse(notExists, "不存在的数据应返回false");

            var allIds = await storage.GetAllWorkflowIdsAsync();
            _logger.LogInformation($"存储的数据总数: {allIds.Count}");
            Assert.AreEqual(2, allIds.Count, "应有2条数据");

            var updatedData = new Dictionary<string, object>
            {
                { "name", "Updated Workflow" },
                { "version", "1.1.0" },
                { "updatedAt", DateTime.UtcNow }
            };
            await storage.StoreAsync("workflow-001", updatedData);
            _logger.LogInformation("更新数据成功");

            var updated = await storage.RetrieveAsync("workflow-001");
            if (updated != null)
            {
                _logger.LogInformation($"更新后数据: name={updated["name"]}, version={updated["version"]}");
                Assert.AreEqual("Updated Workflow", updated["name"], "更新后名称应匹配");
            }

            await storage.DeleteAsync("workflow-001");
            await storage.DeleteAsync("workflow-002");

            allIds = await storage.GetAllWorkflowIdsAsync();
            _logger.LogInformation($"删除后数据数量: {allIds.Count}");
            Assert.AreEqual(0, allIds.Count, "删除后应有0条数据");

            _logger.LogInformation("存储模块示例完成");
        }
        #endregion

        #region 5. 引用管理示例
                [TestMethod]
        public async Task ReferenceManagement_ShouldManageResourceReferences()
        {
            _logger.LogInformation("\n--- 5. 引用管理示例 ---");

            var innerManager = new DefaultUniversalReferenceManager();
            await innerManager.InitializeAsync();
            var referenceManager = new GraphReferenceManager(innerManager, _logger as ILogger<GraphReferenceManager>);

            var workflowResource = new ResourceIdentifier { ResourceType = "workflow", Id = "order-processing" };
            var actionResource = new ResourceIdentifier { ResourceType = "action", Id = "send-email" };
            var templateResource = new ResourceIdentifier { ResourceType = "template", Id = "email-template" };

            await referenceManager.EstablishReferenceAsync(workflowResource, actionResource, "uses");
            _logger.LogInformation("建立引用关系: workflow -> action");

            await referenceManager.EstablishReferenceAsync(actionResource, templateResource, "depends_on");
            _logger.LogInformation("建立引用关系: action -> template");

            var workflowReferences = await referenceManager.GetReferencesAsync(workflowResource, "uses");
            _logger.LogInformation($"获取到 {workflowReferences.Count} 个引用");
            Assert.AreEqual(1, workflowReferences.Count, "工作流应有1个引用");

            var actionReferences = await referenceManager.GetReferencesAsync(actionResource, "depends_on");
            _logger.LogInformation($"action资源引用数量: {actionReferences.Count}");
            Assert.AreEqual(1, actionReferences.Count, "action应有1个引用");

            await referenceManager.RemoveAllReferencesAsync(workflowResource);
            _logger.LogInformation("删除工作流引用关系");

            workflowReferences = await referenceManager.GetReferencesAsync(workflowResource, "uses");
            _logger.LogInformation($"删除后工作流引用数量: {workflowReferences.Count}");
            Assert.AreEqual(0, workflowReferences.Count, "删除后应有0个引用");

            _logger.LogInformation("引用管理示例完成");
        }
        #endregion

        #region 6. 事件驱动示例
                [TestMethod]
        public async Task EventDriven_ShouldTriggerEventsOnResourceChanges()
        {
            _logger.LogInformation("\n--- 6. 事件驱动示例 ---");

            var innerManager = UniversalReferenceManagerFactory.Create();
            await innerManager.InitializeAsync();

            var observableManager = new ObservableReferenceManager(innerManager);

            int eventCount = 0;

            observableManager.OnResourceAdded += (sender, args) =>
            {
                eventCount++;
                _logger.LogInformation($"资源添加事件 #{eventCount}: {args.Resource.ResourceType}/{args.Resource.Id}");
            };

            observableManager.OnResourceUpdated += (sender, args) =>
            {
                eventCount++;
                _logger.LogInformation($"资源更新事件 #{eventCount}: {args.Resource.ResourceType}/{args.Resource.Id}");
            };

            observableManager.OnResourceRemoved += (sender, args) =>
            {
                eventCount++;
                _logger.LogInformation($"资源删除事件 #{eventCount}: {args.Resource.ResourceType}/{args.Resource.Id}");
            };

            var resourceId = new ResourceIdentifier { ResourceType = "document", Id = "doc-001" };
            await observableManager.AddResourceAsync(resourceId, new Dictionary<string, object>
            {
                { "title", "测试文档" },
                { "size", 1024 }
            });

            await observableManager.UpdateResourceAsync(resourceId, new Dictionary<string, object>
            {
                { "title", "更新后的文档" },
                { "version", 2 }
            });

            var resourceId2 = new ResourceIdentifier { ResourceType = "document", Id = "doc-002" };
            await observableManager.AddResourceAsync(resourceId2, new Dictionary<string, object>
            {
                { "title", "第二个文档" },
                { "size", 2048 }
            });

            await observableManager.RemoveResourceAsync(resourceId);
            await observableManager.RemoveResourceAsync(resourceId2);

            _logger.LogInformation($"总事件触发次数: {eventCount}");
            Assert.AreEqual(5, eventCount, "应有5个事件触发（添加2 + 更新1 + 删除2）");

            observableManager.Dispose();
            _logger.LogInformation("事件驱动示例完成");
        }
        #endregion

        #region 7. LiteDB持久化示例
                [TestMethod]
        public async Task LiteDBPersistence_ShouldPersistNodesAndEdges()
        {
            _logger.LogInformation("\n--- 7. LiteDB持久化示例 ---");

            using var persistence = new LiteDBPersistence<string, Dictionary<string, object>, Dictionary<string, object>>();

            var dbDirectory = Path.GetDirectoryName(TestDatabaseName);
            if (!string.IsNullOrEmpty(dbDirectory) && !System.IO.Directory.Exists(dbDirectory))
            {
                System.IO.Directory.CreateDirectory(dbDirectory);
                _logger.LogInformation($"创建数据库目录: {dbDirectory}");
            }

            await persistence.InitializeAsync(TestDatabaseName);
            _logger.LogInformation($"初始化数据库: {TestDatabaseName}");

            var nodeData1 = new Dictionary<string, object>
            {
                { "name", "Start Node" },
                { "type", "start" },
                { "config", new Dictionary<string, object> { { "timeout", 30 } } }
            };
            await persistence.SaveNodeAsync("node-001", nodeData1, "START");
            _logger.LogInformation("保存节点: node-001");

            var nodeData2 = new Dictionary<string, object>
            {
                { "name", "Action Node" },
                { "type", "action" },
                { "actionType", "submit" },
                { "config", new Dictionary<string, object> { { "retryCount", 3 } } }
            };
            await persistence.SaveNodeAsync("node-002", nodeData2, "ACTION");
            _logger.LogInformation("保存节点: node-002");

            var nodeData3 = new Dictionary<string, object>
            {
                { "name", "End Node" },
                { "type", "end" },
                { "result", "success" }
            };
            await persistence.SaveNodeAsync("node-003", nodeData3, "END");
            _logger.LogInformation("保存节点: node-003");

            var edgeData1 = new Dictionary<string, object>
            {
                { "condition", "status == 'approved'" },
                { "priority", 1 }
            };
            await persistence.SaveEdgeAsync("edge-001", "node-001", "node-002", isDirected: true, edgeData1);
            _logger.LogInformation("保存边: edge-001 (node-001 -> node-002)");

            var edgeData2 = new Dictionary<string, object>
            {
                { "condition", "action_completed" },
                { "priority", 2 }
            };
            await persistence.SaveEdgeAsync("edge-002", "node-002", "node-003", isDirected: true, edgeData2);
            _logger.LogInformation("保存边: edge-002 (node-002 -> node-003)");

            var loadedNode = await persistence.LoadNodeAsync("node-001");
            Assert.IsNotNull(loadedNode, "应能加载节点");
            if (loadedNode != null)
            {
                _logger.LogInformation($"加载节点: name={loadedNode["name"]}, type={loadedNode["type"]}");
                Assert.AreEqual("Start Node", loadedNode["name"], "节点名称应匹配");
            }

            var loadedEdge = await persistence.LoadEdgeAsync("edge-001");
            Assert.IsNotNull(loadedEdge, "应能加载边");
            if (loadedEdge != null)
            {
                _logger.LogInformation($"加载边: condition={loadedEdge["condition"]}, priority={loadedEdge["priority"]}");
            }

            var nodeExists = await persistence.ContainsNodeAsync("node-001");
            _logger.LogInformation($"节点 node-001 存在: {nodeExists}");
            Assert.IsTrue(nodeExists, "节点应存在");

            var nodeNotExists = await persistence.ContainsNodeAsync("node-999");
            _logger.LogInformation($"节点 node-999 存在: {nodeNotExists}");
            Assert.IsFalse(nodeNotExists, "不存在的节点应返回false");

            var allNodes = await persistence.GetAllNodeKeysAsync();
            _logger.LogInformation($"节点总数: {allNodes.Count}");
            Assert.AreEqual(3, allNodes.Count, "应有3个节点");

            var allEdges = await persistence.GetAllEdgeIdsAsync();
            _logger.LogInformation($"边总数: {allEdges.Count}");
            Assert.AreEqual(2, allEdges.Count, "应有2条边");

            var updatedNodeData = new Dictionary<string, object>
            {
                { "name", "Updated Start Node" },
                { "type", "start" },
                { "config", new Dictionary<string, object> { { "timeout", 60 }, { "enabled", true } } }
            };
            await persistence.SaveNodeAsync("node-001", updatedNodeData, "START");
            _logger.LogInformation("更新节点: node-001");

            loadedNode = await persistence.LoadNodeAsync("node-001");
            if (loadedNode != null)
            {
                _logger.LogInformation($"更新后节点: name={loadedNode["name"]}");
                Assert.AreEqual("Updated Start Node", loadedNode["name"], "更新后名称应匹配");
            }

            await persistence.RemoveNodeAsync("node-003");
            _logger.LogInformation("删除节点: node-003");

            allNodes = await persistence.GetAllNodeKeysAsync();
            _logger.LogInformation($"删除后节点数量: {allNodes.Count}");
            Assert.AreEqual(2, allNodes.Count, "删除后应有2个节点");

            await persistence.ClearAllAsync();
            _logger.LogInformation("LiteDB持久化示例完成");
        }
        #endregion

        #region 8. 通用引用管理器综合测试
                [TestMethod]
        public async Task UniversalReferenceManager_ShouldManageResourcesAndReferences()
        {
            _logger.LogInformation("\n--- 8. 通用引用管理器综合测试 ---");

            var isolatedDir = Path.Combine(Path.GetTempPath(), "UR_CompleteUsage_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(isolatedDir);
            try
            {
                UniversalReferenceManagerFactory.SetDataDirectory(isolatedDir);
                var manager = UniversalReferenceManagerFactory.Create();
                await manager.InitializeAsync();

                var workflowResource = new ResourceIdentifier { ResourceType = "workflow", Id = "test-workflow" };
                var actionResource = new ResourceIdentifier { ResourceType = "action", Id = "test-action" };
                var templateResource = new ResourceIdentifier { ResourceType = "template", Id = "test-template" };
                var dataResource = new ResourceIdentifier { ResourceType = "data", Id = "test-data" };

                await manager.AddResourceAsync(workflowResource, new Dictionary<string, object>
                {
                    { "name", "测试工作流" },
                    { "version", "1.0.0" },
                    { "createdAt", DateTime.UtcNow }
                }, "WORKFLOW");

                await manager.AddResourceAsync(actionResource, new Dictionary<string, object>
                {
                    { "name", "测试操作" },
                    { "actionType", "submit" }
                }, "ACTION");

                await manager.AddResourceAsync(templateResource, new Dictionary<string, object>
                {
                    { "name", "测试模板" },
                    { "format", "json" }
                }, "TEMPLATE");

                _logger.LogInformation("添加测试资源完成");

                await manager.AddReferenceAsync(workflowResource, actionResource, ReferenceType.Dependency, "step1", "工作流依赖操作", 0.8);
                await manager.AddReferenceAsync(actionResource, templateResource, ReferenceType.Uses, "process", "操作使用模板", 0.9);
                await manager.AddReferenceAsync(workflowResource, templateResource, ReferenceType.Other, "flow", "间接依赖", 0.5);

                _logger.LogInformation("添加测试引用完成");

                var retrieved = await manager.GetResourceAsync(workflowResource);
                Assert.IsNotNull(retrieved, "应能获取到资源");
                if (retrieved != null)
                {
                    _logger.LogInformation($"获取资源: {retrieved.Identifier.ResourceType}/{retrieved.Identifier.Id}, Symbol={retrieved.Symbol}");
                }

                var exists = manager.ContainsResource(workflowResource);
                _logger.LogInformation($"资源存在: {exists}");
                Assert.IsTrue(exists, "资源应存在");

                var allResources = await manager.GetAllResourcesAsync();
                _logger.LogInformation($"资源总数: {allResources.Count}");
                Assert.AreEqual(3, allResources.Count, "应有3个资源");

                var references = await manager.GetReferencesAsync(workflowResource);
                _logger.LogInformation($"工作流引用数量: {references.Count}");
                Assert.AreEqual(2, references.Count, "工作流应有2个引用");

                var dependencies = await manager.GetDependenciesAsync(workflowResource, maxDepth: 2);
                _logger.LogInformation($"工作流依赖数量: {dependencies.Count}");

                var dependents = await manager.GetDependentsAsync(templateResource);
                _logger.LogInformation($"模板被依赖数量: {dependents.Count}");

                var workflows = await manager.GetResourcesByTypeAsync("workflow");
                _logger.LogInformation($"工作流类型资源数量: {workflows.Count}");
                Assert.AreEqual(1, workflows.Count, "应有1个工作流类型资源");

                await manager.UpdateResourceAsync(workflowResource, new Dictionary<string, object>
                {
                    { "name", "更新后的工作流" },
                    { "version", "1.1.0" }
                }, "WORKFLOW_V2");
                _logger.LogInformation("更新资源完成");

                retrieved = await manager.GetResourceAsync(workflowResource);
                if (retrieved != null)
                {
                    _logger.LogInformation($"更新后资源: Symbol={retrieved.Symbol}");
                    Assert.AreEqual("WORKFLOW_V2", retrieved.Symbol, "更新后Symbol应匹配");
                }

                await manager.RemoveResourceAsync(actionResource);
                _logger.LogInformation("删除资源完成");

                references = await manager.GetReferencesAsync(workflowResource);
                _logger.LogInformation($"删除后工作流引用数量: {references.Count}");

                await manager.CleanupNonExistentResourcesAsync();
                _logger.LogInformation("通用引用管理器测试完成");
            }
            finally
            {
                try { Directory.Delete(isolatedDir, recursive: true); } catch { }
            }
        }
        #endregion

        #region 9. 图数据库存储测试
                [TestMethod]
        public async Task GraphDatabaseStorage_ShouldStoreAndRetrieveGraphData()
        {
            _logger.LogInformation("\n--- 9. 图数据库存储测试 ---");

            var graphStorage = GraphDatabaseStorageFactory.Create(_logger);
            await graphStorage.InitializeAsync();

            var resourceId1 = new ResourceIdentifier { ResourceType = "node", Id = "graph-node-001" };
            var resourceId2 = new ResourceIdentifier { ResourceType = "node", Id = "graph-node-002" };
            var resourceId3 = new ResourceIdentifier { ResourceType = "edge", Id = "graph-edge-001" };

            await graphStorage.StoreResourceAsync(resourceId1, new Dictionary<string, object>
            {
                { "label", "Start" },
                { "type", "initial" },
                { "properties", new Dictionary<string, object> { { "x", 100 }, { "y", 200 } } }
            }, "START_NODE");

            await graphStorage.StoreResourceAsync(resourceId2, new Dictionary<string, object>
            {
                { "label", "End" },
                { "type", "terminal" },
                { "properties", new Dictionary<string, object> { { "x", 300 }, { "y", 200 } } }
            }, "END_NODE");

            await graphStorage.StoreResourceAsync(resourceId3, new Dictionary<string, object>
            {
                { "source", "graph-node-001" },
                { "target", "graph-node-002" },
                { "weight", 1.0 }
            }, "EDGE");

            _logger.LogInformation("存储图数据完成");

            var retrieved1 = await graphStorage.GetResourceAsync(resourceId1);
            Assert.IsNotNull(retrieved1, "应能获取到图节点1");
            if (retrieved1 != null)
            {
                _logger.LogInformation($"获取资源1: {retrieved1.Identifier.Id}, Symbol={retrieved1.Symbol}");
            }

            var retrieved2 = await graphStorage.GetResourceAsync(resourceId2);
            Assert.IsNotNull(retrieved2, "应能获取到图节点2");
            if (retrieved2 != null)
            {
                _logger.LogInformation($"获取资源2: {retrieved2.Identifier.Id}, Symbol={retrieved2.Symbol}");
            }

            var allNodes = await graphStorage.GetResourcesByTypeAsync("node");
            _logger.LogInformation($"图数据库节点资源总数: {allNodes.Count}");

            var nodes = await graphStorage.GetResourcesByTypeAsync("node");
            _logger.LogInformation($"节点类型资源数量: {nodes.Count}");
            Assert.AreEqual(2, nodes.Count, "应有2个节点类型资源");

            var edges = await graphStorage.GetResourcesByTypeAsync("edge");
            _logger.LogInformation($"边类型资源数量: {edges.Count}");
            Assert.AreEqual(1, edges.Count, "应有1个边类型资源");

            await graphStorage.StoreResourceAsync(resourceId1, new Dictionary<string, object>
            {
                { "label", "Updated Start" },
                { "type", "initial" },
                { "properties", new Dictionary<string, object> { { "x", 150 }, { "y", 250 }, { "color", "red" } } }
            }, "START_NODE_V2");
            _logger.LogInformation("更新图数据完成");

            retrieved1 = await graphStorage.GetResourceAsync(resourceId1);
            if (retrieved1 != null && retrieved1.Metadata.TryGetValue("label", out var label))
            {
                _logger.LogInformation($"更新后标签: {label}");
            }

            await graphStorage.DeleteResourceAsync(resourceId3);
            _logger.LogInformation("删除边数据完成");

            edges = await graphStorage.GetResourcesByTypeAsync("edge");
            _logger.LogInformation($"删除后边数量: {edges.Count}");
            Assert.AreEqual(0, edges.Count, "删除后应有0条边");

            await graphStorage.DeleteResourceAsync(resourceId1);
            await graphStorage.DeleteResourceAsync(resourceId2);
            _logger.LogInformation("图数据库存储测试完成");
        }
        #endregion

        #region 10. 测试环境清理
                [TestCleanup]
        public async Task CleanupTestEnvironment()
        {
            _logger.LogInformation("\n--- 10. 清理测试环境 ---");

            try
            {
                if (System.IO.File.Exists(TestDatabaseName))
                {
                    System.IO.File.Delete(TestDatabaseName);
                    _logger.LogInformation($"删除测试数据库: {TestDatabaseName}");
                }

                if (System.IO.Directory.Exists(TestDataDirectory))
                {
                    System.IO.Directory.Delete(TestDataDirectory, recursive: true);
                    _logger.LogInformation($"删除测试目录: {TestDataDirectory}");
                }

                _logger.LogInformation("测试环境清理完成");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"清理测试环境时发生错误: {ex.Message}");
            }
        }
        #endregion
    }
}
