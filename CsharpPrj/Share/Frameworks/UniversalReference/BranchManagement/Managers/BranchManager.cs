using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniversalWorkflowFramework.BranchManagement.Models;
using UniversalWorkflowFramework.BranchManagement.Validators;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.Diagnostics;
using UniversalWorkflowFramework.Reference.Models;
using UniversalWorkflowFramework.Storage.Interfaces;

namespace UniversalWorkflowFramework.BranchManagement.Managers
{
    public class BranchManager<TBranchGraph> : IBranchManager<TBranchGraph>
        where TBranchGraph : BranchGraph, new()
    {
        private const string ResourceTypeBranch = "branch";
        private const string ResourceTypeNode = "node";
        private const string ResourceTypeEdge = "edge";
        private const string RelationshipContainsNode = "contains_node";
        private const string RelationshipContainsEdge = "contains_edge";
        private const string RelationshipStartsFrom = "starts_from";
        private const string RelationshipEndsAt = "ends_at";

        private readonly IWorkflowStorage<TBranchGraph> _branchStorage;
        private readonly IUniversalReferenceManager _referenceManager;
        private readonly BranchValidator _branchValidator;

        public BranchManager(IWorkflowStorage<TBranchGraph> branchStorage, IUniversalReferenceManager referenceManager)
        {
            _branchStorage = branchStorage;
            _referenceManager = referenceManager ?? throw new ArgumentNullException(nameof(referenceManager));
            _branchValidator = new BranchValidator();
        }

        public BranchManager(IWorkflowStorage<TBranchGraph> branchStorage)
            : this(branchStorage, UniversalReferenceManagerFactory.Create())
        {
        }

        public async Task InitializeAsync()
        {
            try
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.Init, new { message = "初始化分支管理器" });
                await _referenceManager.InitializeAsync();
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.InitSuccess, new { message = "分支管理器初始化成功" });
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.InitError, new { message = "初始化分支管理器失败", error = ex.Message });
                throw;
            }
        }

        public async Task<string> RegisterBranchAsync(string branchId, TBranchGraph branchGraph)
        {
            try
            {
                branchGraph.BranchId = branchId;
                branchGraph.UpdatedAt = DateTime.UtcNow;

                await StoreBranchToGraphDatabaseAsync(branchId, branchGraph);

                await _branchStorage.StoreAsync(branchId, branchGraph);

                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.RegisterSuccess, new { message = $"注册分支成功: {branchId}", branchId });
                return branchId;
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.RegisterError, new { message = $"注册分支失败: {branchId}", branchId, error = ex.Message });
                throw;
            }
        }

        public async Task<TBranchGraph?> GetBranchAsync(string branchId)
        {
            try
            {
                var branchGraph = await RetrieveBranchFromGraphDatabaseAsync(branchId);
                if (branchGraph != null)
                {
                    return branchGraph;
                }

                branchGraph = await _branchStorage.RetrieveAsync(branchId);
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.GetSuccess, new { message = $"从本地存储获取分支成功: {branchId}", branchId });
                return branchGraph;
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.GetError, new { message = $"获取分支失败: {branchId}", branchId, error = ex.Message });
                throw;
            }
        }

        public async Task RemoveBranchAsync(string branchId)
        {
            try
            {
                await RemoveBranchFromGraphDatabaseAsync(branchId);

                await _branchStorage.DeleteAsync(branchId);

                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.RemoveSuccess, new { message = $"删除分支成功: {branchId}", branchId });
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.RemoveError, new { message = $"删除分支失败: {branchId}", branchId, error = ex.Message });
                throw;
            }
        }

        public async Task<ValidationResult> ValidateBranchAsync(string branchId)
        {
            try
            {
                var branchGraph = await GetBranchAsync(branchId);
                if (branchGraph == null)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Message = $"分支不存在: {branchId}"
                    };
                }

                var validationResult = _branchValidator.ValidateBranchIntegrity(branchGraph);
                if (!validationResult.IsValid)
                {
                    return validationResult;
                }

                validationResult = _branchValidator.ValidateBranchFunctionality(branchGraph);
                if (!validationResult.IsValid)
                {
                    return validationResult;
                }

                validationResult = await ValidateBranchInGraphDatabaseAsync(branchId);
                if (!validationResult.IsValid)
                {
                    return validationResult;
                }

                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.ValidateSuccess, new { message = $"验证分支成功: {branchId}", branchId });
                return new ValidationResult
                {
                    IsValid = true,
                    Message = $"分支验证通过: {branchId}",
                    Details = new Dictionary<string, object>
                    {
                        { "branchId", branchId },
                        { "validationSource", "GraphLogic" }
                    }
                };
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.ValidateError, new { message = $"验证分支失败: {branchId}", branchId, error = ex.Message });
                return new ValidationResult
                {
                    IsValid = false,
                    Message = $"验证分支时出错: {ex.Message}"
                };
            }
        }

        public async Task<List<string>> GetAllBranchIdsAsync()
        {
            try
            {
                var branchIds = await GetAllBranchIdsFromGraphDatabaseAsync();
                if (branchIds.Count > 0)
                {
                    return branchIds;
                }

                branchIds = await _branchStorage.GetAllWorkflowIdsAsync();
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.ListSuccess, new { message = $"从本地存储获取所有分支ID成功，共 {branchIds.Count} 个分支", count = branchIds.Count });
                return branchIds;
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.ListError, new { message = "获取所有分支ID失败", error = ex.Message });
                throw;
            }
        }

        public async Task<bool> BranchExistsAsync(string branchId)
        {
            try
            {
                var branchResourceId = new ResourceIdentifier { ResourceType = ResourceTypeBranch, Id = branchId };
                var branchResource = await _referenceManager.GetResourceAsync(branchResourceId);
                if (branchResource != null)
                {
                    UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.ExistsGraph, new { message = $"从图数据库检查分支存在性: {branchId} - true", branchId, exists = true });
                    return true;
                }

                var exists = await _branchStorage.ExistsAsync(branchId);
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.ExistsLocal, new { message = $"从本地存储检查分支存在性: {branchId} - {exists}", branchId, exists });
                return exists;
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.ExistsError, new { message = $"检查分支存在性失败: {branchId}", branchId, error = ex.Message });
                return false;
            }
        }

        private async Task StoreBranchToGraphDatabaseAsync(string branchId, TBranchGraph branchGraph)
        {
            try
            {
                var branchResourceId = new ResourceIdentifier { ResourceType = ResourceTypeBranch, Id = branchId };
                var branchData = new Dictionary<string, object>
                {
                    { "branchId", branchId },
                    { "name", branchGraph.Name },
                    { "description", branchGraph.Description },
                    { "updatedAt", branchGraph.UpdatedAt },
                    { "nodeCount", branchGraph.Nodes.Count },
                    { "edgeCount", branchGraph.Edges.Count }
                };

                await _referenceManager.AddResourceAsync(branchResourceId, branchData, branchId);

                foreach (var node in branchGraph.Nodes)
                {
                    var nodeResourceId = new ResourceIdentifier { ResourceType = ResourceTypeNode, Id = node.NodeId };
                    var nodeData = new Dictionary<string, object>
                    {
                        { "nodeId", node.NodeId },
                        { "type", node.Type.ToString() },
                        { "name", node.Name },
                        { "properties", node.Data }
                    };

                    await _referenceManager.AddResourceAsync(nodeResourceId, nodeData, node.NodeId);
                    await _referenceManager.AddReferenceAsync(branchResourceId, nodeResourceId, ReferenceType.Other, description: RelationshipContainsNode);
                }

                foreach (var edge in branchGraph.Edges)
                {
                    var edgeResourceId = new ResourceIdentifier { ResourceType = ResourceTypeEdge, Id = edge.EdgeId };
                    var edgeData = new Dictionary<string, object>
                    {
                        { "edgeId", edge.EdgeId },
                        { "sourceNodeId", edge.SourceNodeId },
                        { "targetNodeId", edge.TargetNodeId },
                        { "type", edge.Type.ToString() },
                        { "condition", edge.Data.TryGetValue("condition", out var condValue) ? condValue : "" }
                    };

                    await _referenceManager.AddResourceAsync(edgeResourceId, edgeData, edge.EdgeId);
                    await _referenceManager.AddReferenceAsync(branchResourceId, edgeResourceId, ReferenceType.Other, description: RelationshipContainsEdge);
                    var sourceNodeId = new ResourceIdentifier { ResourceType = ResourceTypeNode, Id = edge.SourceNodeId };
                    await _referenceManager.AddReferenceAsync(edgeResourceId, sourceNodeId, ReferenceType.Other, description: RelationshipStartsFrom);
                    var targetNodeId = new ResourceIdentifier { ResourceType = ResourceTypeNode, Id = edge.TargetNodeId };
                    await _referenceManager.AddReferenceAsync(edgeResourceId, targetNodeId, ReferenceType.Other, description: RelationshipEndsAt);
                }

                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.StoreSuccess, new { message = $"存储分支到图数据库成功: {branchId}", branchId });
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.StoreError, new { message = $"存储分支到图数据库失败: {branchId}", branchId, error = ex.Message });
                throw;
            }
        }

        private async Task<TBranchGraph?> RetrieveBranchFromGraphDatabaseAsync(string branchId)
        {
            try
            {
                var branchResourceId = new ResourceIdentifier { ResourceType = ResourceTypeBranch, Id = branchId };
                var branchResource = await _referenceManager.GetResourceAsync(branchResourceId);
                if (branchResource == null)
                {
                    return null;
                }

                var branchData = branchResource.Metadata;
                var branchGraph = new TBranchGraph
                {
                    BranchId = branchId,
                    Name = branchData.TryGetValue("name", out var nameValue) ? nameValue?.ToString() ?? string.Empty : string.Empty,
                    Description = branchData.TryGetValue("description", out var descValue) ? descValue?.ToString() ?? string.Empty : string.Empty,
                    UpdatedAt = branchData.TryGetValue("updatedAt", out var updatedAtValue) ? (updatedAtValue is DateTime dt ? dt : DateTime.UtcNow) : DateTime.UtcNow,
                    Nodes = new List<BranchNode>(),
                    Edges = new List<BranchEdge>()
                };

                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.RetrieveSuccess, new { message = $"从图数据库检索分支成功: {branchId}", branchId });
                return branchGraph;
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.RetrieveError, new { message = $"从图数据库检索分支失败: {branchId}", branchId, error = ex.Message });
                return null;
            }
        }

        private async Task RemoveBranchFromGraphDatabaseAsync(string branchId)
        {
            try
            {
                var branchResourceId = new ResourceIdentifier { ResourceType = ResourceTypeBranch, Id = branchId };
                await _referenceManager.RemoveResourceAsync(branchResourceId);
                await RemoveAllReferencesAsync(branchResourceId);

                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.DeleteSuccess, new { message = $"从图数据库删除分支成功: {branchId}", branchId });
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.DeleteError, new { message = $"从图数据库删除分支失败: {branchId}", branchId, error = ex.Message });
                throw;
            }
        }

        private async Task RemoveAllReferencesAsync(ResourceIdentifier resourceId)
        {
            var references = await _referenceManager.GetReferencesAsync(resourceId, includeIncoming: true);
            foreach (var (source, target, type, _, _, _, _) in references)
            {
                await _referenceManager.RemoveReferenceAsync(source, target, type);
            }
        }

        private async Task<ValidationResult> ValidateBranchInGraphDatabaseAsync(string branchId)
        {
            try
            {
                var branchResourceId = new ResourceIdentifier { ResourceType = ResourceTypeBranch, Id = branchId };
                var branchResource = await _referenceManager.GetResourceAsync(branchResourceId);
                if (branchResource == null)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Message = "分支在图数据库中不存在"
                    };
                }

                var branchData = branchResource.Metadata;
                var nodeCount = branchData.TryGetValue("nodeCount", out var ncValue) ? Convert.ToInt32(ncValue) : 0;
                var edgeCount = branchData.TryGetValue("edgeCount", out var ecValue) ? Convert.ToInt32(ecValue) : 0;

                if (nodeCount == 0)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Message = "分支在图数据库中缺少节点"
                    };
                }

                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.GraphValidateSuccess, new { message = $"验证分支在图数据库中的状态成功: {branchId}", branchId });
                return new ValidationResult
                {
                    IsValid = true,
                    Message = "分支在图数据库中的状态验证通过"
                };
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.GraphValidateError, new { message = $"验证分支在图数据库中的状态失败: {branchId}", branchId, error = ex.Message });
                return new ValidationResult
                {
                    IsValid = false,
                    Message = $"验证分支在图数据库中的状态时出错: {ex.Message}"
                };
            }
        }

        private async Task<List<string>> GetAllBranchIdsFromGraphDatabaseAsync()
        {
            try
            {
                var resources = await _referenceManager.GetResourcesByTypeAsync(ResourceTypeBranch);
                var branchIds = new List<string>();

                foreach (var resource in resources)
                {
                    branchIds.Add(resource.Identifier.Id);
                }

                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.GraphListSuccess, new { message = $"从图数据库获取所有分支ID成功，共 {branchIds.Count} 个分支", count = branchIds.Count });
                return branchIds;
            }
            catch (Exception ex)
            {
                UniversalWorkflowFrameworkDiagnosticContext.Record(DiagnosticEvents.Branch.GraphListError, new { message = "从图数据库获取所有分支ID失败", error = ex.Message });
                return new List<string>();
            }
        }
    }

    public static class BranchManagerFactory
    {
        public static IBranchManager<TBranchGraph> Create<TBranchGraph>(IWorkflowStorage<TBranchGraph> branchStorage)
            where TBranchGraph : BranchGraph, new()
        {
            return new BranchManager<TBranchGraph>(branchStorage);
        }

        public static IBranchManager<TBranchGraph> Create<TBranchGraph>(IWorkflowStorage<TBranchGraph> branchStorage, IUniversalReferenceManager referenceManager)
            where TBranchGraph : BranchGraph, new()
        {
            return new BranchManager<TBranchGraph>(branchStorage, referenceManager);
        }
    }
}
namespace UniversalWorkflowFramework.BranchManagement.Managers
{
    public interface IBranchManager<TBranchGraph>
    {
        Task InitializeAsync();

        Task<string> RegisterBranchAsync(string branchId, TBranchGraph branchGraph);

        Task<TBranchGraph?> GetBranchAsync(string branchId);

        Task RemoveBranchAsync(string branchId);

        Task<ValidationResult> ValidateBranchAsync(string branchId);

        Task<List<string>> GetAllBranchIdsAsync();

        Task<bool> BranchExistsAsync(string branchId);
    }
}