
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.Diagnostics;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;
using RefModels = UniversalWorkflowFramework.Reference.Models;

namespace UniversalWorkflowFramework.BatchIntegration
{
    public class BatchRegistry
    {
        private readonly IUniversalReferenceManager _refManager;

        public BatchRegistry(IUniversalReferenceManager refManager)
        {
            _refManager = refManager ?? throw new ArgumentNullException(nameof(refManager));
        }

        public async Task RegisterBatchAsync(BatchMetadata metadata)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            BatchManager.Instance.RegisterBatch(metadata.BatchId, metadata);

            var resourceId = new Core.Models.ResourceIdentifier
            {
                ResourceType = "Batch",
                Id = metadata.BatchId,
                Name = metadata.Name
            };

            await _refManager.AddResourceAsync(resourceId, ConvertToMetadataDict(metadata));

        }

        public async Task UpdateBatchAsync(BatchMetadata metadata)
        {
            if (metadata == null) return;

            var resourceId = new Core.Models.ResourceIdentifier
            {
                ResourceType = "Batch",
                Id = metadata.BatchId,
                Name = metadata.Name
            };

            await _refManager.UpdateResourceAsync(resourceId, ConvertToMetadataDict(metadata));

        }


        public async Task LinkBatchesAsync(string fromBatchId, string toBatchId, 
            RefModels.ReferenceType relationType = RefModels.ReferenceType.Dependency, 
            string? description = null)
        {
            var source = new Core.Models.ResourceIdentifier { ResourceType = "Batch", Id = fromBatchId };
            var target = new Core.Models.ResourceIdentifier { ResourceType = "Batch", Id = toBatchId };

            await _refManager.AddReferenceAsync(source, target, relationType, 
                description: description ?? $"批次 {fromBatchId} 关联到 {toBatchId}");
        }

        public async Task<List<string>> GetBatchDependenciesAsync(string batchId, int maxDepth = 5)
        {
            var resourceId = new Core.Models.ResourceIdentifier { ResourceType = "Batch", Id = batchId };
            var deps = await _refManager.GetDependenciesAsync(resourceId, maxDepth);
            return deps.Select(d => d.Id ?? "").Where(id => !string.IsNullOrEmpty(id)).ToList();
        }

        public async Task<List<string>> GetBatchDependentsAsync(string batchId, int maxDepth = 5)
        {
            var resourceId = new Core.Models.ResourceIdentifier { ResourceType = "Batch", Id = batchId };
            var deps = await _refManager.GetDependentsAsync(resourceId, maxDepth);
            return deps.Select(d => d.Id ?? "").Where(id => !string.IsNullOrEmpty(id)).ToList();
        }

        public Task<List<BatchMetadata>> GetAllBatchMetadataAsync()
        {
            var result = BatchManager.Instance.GetAllBatches().ToList();
            return Task.FromResult(result);
        }


        public async Task RemoveBatchAsync(string batchId)
        {
            BatchManager.Instance.CleanupBatch(batchId);

            var resourceId = new Core.Models.ResourceIdentifier { ResourceType = "Batch", Id = batchId };
            await _refManager.RemoveResourceAsync(resourceId);
        }


        private static Dictionary<string, object> ConvertToMetadataDict(BatchMetadata metadata)
        {
            return new Dictionary<string, object>
            {
                ["StartTime"] = metadata.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                ["EndTime"] = metadata.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                ["Source"] = metadata.Source ?? "",
                ["WorkflowId"] = metadata.WorkflowId ?? "",
                ["Status"] = metadata.Status.ToString(),
                ["LogCount"] = metadata.LogCount,
                ["ErrorCount"] = metadata.ErrorCount,
                ["WarnCount"] = metadata.WarnCount,
                ["Description"] = metadata.Description ?? ""
            };
        }
    }
}
