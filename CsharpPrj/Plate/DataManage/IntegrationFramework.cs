#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WorkflowFramework.Core
{
    public interface IWorkflowVersion
    {
        string Id { get; }
        string WorkflowId { get; }
        string Version { get; }
        string JsonContentPath { get; }
        DateTime CreatedAt { get; }
        IReadOnlyDictionary<string, object> Metadata { get; }
    }

    public interface ICodeVersion
    {
        string Id { get; }
        string Name { get; }
        string ContentPath { get; }
        string ContentHash { get; }
        string Language { get; }
        long SizeBytes { get; }
    }

    public interface IWorkflowRun
    {
        string Id { get; }
        string WorkflowVersionId { get; }
        string Status { get; }
        DateTime StartedAt { get; }
        DateTime? CompletedAt { get; }
        IReadOnlyDictionary<string, object> Inputs { get; }
        IReadOnlyDictionary<string, object> Outputs { get; }
    }

    public interface IRunMetric
    {
        string RunId { get; }
        string StepName { get; }
        string MetricName { get; }
        double Value { get; }
        DateTime Timestamp { get; }
    }

    public record WorkflowVersionRecord : IWorkflowVersion
    {
        public string Id { get; init; } = string.Empty;
        public string WorkflowId { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string JsonContentPath { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
    }

    public record CodeVersionRecord : ICodeVersion
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string ContentPath { get; init; } = string.Empty;
        public string ContentHash { get; init; } = string.Empty;
        public string Language { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
    }

    public record WorkflowRunRecord : IWorkflowRun
    {
        public string Id { get; init; } = string.Empty;
        public string WorkflowVersionId { get; init; } = string.Empty;
        public string Status { get; init; } = "Pending";
        public DateTime StartedAt { get; init; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; init; }
        public IReadOnlyDictionary<string, object> Inputs { get; init; } = new Dictionary<string, object>();
        public IReadOnlyDictionary<string, object> Outputs { get; init; } = new Dictionary<string, object>();
    }

    public record RunMetricRecord : IRunMetric
    {
        public string RunId { get; init; } = string.Empty;
        public string StepName { get; init; } = string.Empty;
        public string MetricName { get; init; } = string.Empty;
        public double Value { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public static class WorkflowRelationshipTypes
    {
        public const string DependsOnCode = "DependsOnCode";
        public const string TriggersRun = "TriggersRun";
        public const string ProducesData = "ProducesData";
        public const string PreviousVersion = "PreviousVersion";
    }

    public static class WorkflowResourceTypes
    {
        public const string WorkflowVersion = "WorkflowVersion";
        public const string CodeVersion = "CodeVersion";
        public const string WorkflowRun = "WorkflowRun";
    }
}

namespace WorkflowFramework.Storage.FileSystem
{
    using WorkflowFramework.Core;

    public interface IFileStorageService
    {
        Task<string> SaveContentAsync(string relativePath, byte[] content, CancellationToken ct = default);
        Task<byte[]> ReadContentAsync(string relativePath, CancellationToken ct = default);
        Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);
        Task DeleteAsync(string relativePath, CancellationToken ct = default);
        Stream OpenReadStream(string relativePath);
    }

    public sealed class LocalFileStorageService : IFileStorageService
    {
        private readonly string _rootDirectory;

        public LocalFileStorageService(string rootDirectory)
        {
            _rootDirectory = Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(_rootDirectory);
        }

        public async Task<string> SaveContentAsync(string relativePath, byte[] content, CancellationToken ct = default)
        {
            var fullPath = GetFullPath(relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(fullPath, content, ct);
            return fullPath;
        }

        public Task<byte[]> ReadContentAsync(string relativePath, CancellationToken ct = default)
            => File.ReadAllBytesAsync(GetFullPath(relativePath), ct);

        public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
            => Task.FromResult(File.Exists(GetFullPath(relativePath)));

        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        {
            var path = GetFullPath(relativePath);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        public Stream OpenReadStream(string relativePath)
            => File.OpenRead(GetFullPath(relativePath));

        private string GetFullPath(string relativePath)
        {
            var full = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath));
            if (!full.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Invalid path: {relativePath}");
            return full;
        }
    }

    public sealed class ContentAddressedCodeStore
    {
        private readonly IFileStorageService _fileStore;
        private const string CodeSubDirectory = "code";

        public ContentAddressedCodeStore(IFileStorageService fileStore) => _fileStore = fileStore;

        public async Task<ICodeVersion> StoreCodeAsync(string logicalName, byte[] content, string language, CancellationToken ct = default)
        {
            var hash = ComputeSha256(content);
            var relativePath = Path.Combine(CodeSubDirectory, hash[..2], hash);
            await _fileStore.SaveContentAsync(relativePath, content, ct);

            return new CodeVersionRecord
            {
                Id = hash,
                Name = logicalName,
                ContentPath = relativePath,
                ContentHash = hash,
                Language = language,
                SizeBytes = content.Length
            };
        }

        public Task<byte[]> RetrieveCodeAsync(string contentHash, CancellationToken ct = default)
            => _fileStore.ReadContentAsync(Path.Combine(CodeSubDirectory, contentHash[..2], contentHash), ct);

        public Task<bool> CodeExistsAsync(string contentHash, CancellationToken ct = default)
            => _fileStore.ExistsAsync(Path.Combine(CodeSubDirectory, contentHash[..2], contentHash), ct);

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}

namespace WorkflowFramework.Storage.Sqlite
{
    using DataManage.Storage;
    using WorkflowFramework.Core;
    using DataManage.Storage.Helpers;

    public interface IRunDataStore
    {
        Task InitializeAsync(CancellationToken ct = default);
        Task SaveRunAsync(IWorkflowRun run, CancellationToken ct = default);
        Task UpdateRunStatusAsync(string runId, string status, DateTime? completedAt = null, CancellationToken ct = default);
        Task SaveMetricsAsync(IEnumerable<IRunMetric> metrics, CancellationToken ct = default);
        Task<IWorkflowRun?> GetRunAsync(string runId, CancellationToken ct = default);
        Task<IReadOnlyList<IRunMetric>> GetMetricsAsync(string runId, string? stepName = null, CancellationToken ct = default);
        Task<IReadOnlyList<IWorkflowRun>> QueryRunsAsync(string workflowVersionId, string? status = null, int limit = 100, CancellationToken ct = default);
    }

    public sealed class SqliteRunDataStore : IRunDataStore
    {
        private readonly SQLiteKeyValueStore _store;
        private const string RunsContainer = "Runs";
        private const string MetricsContainer = "RunMetrics";
        private readonly ISerializer _serializer;

        public SqliteRunDataStore(string dbPath, ISerializer? serializer = null)
        {
            _serializer = serializer ?? Serializer.Default;
            _store = new SQLiteKeyValueStore("RunData", _serializer);
            _store.SetConnectionString(dbPath);
        }

        public async Task InitializeAsync(CancellationToken ct = default)
            => await _store.InitializeAsync(ct);

        public async Task SaveRunAsync(IWorkflowRun run, CancellationToken ct = default)
        {
            var data = new Dictionary<string, object>
            {
                ["Id"] = run.Id,
                ["WorkflowVersionId"] = run.WorkflowVersionId,
                ["Status"] = run.Status,
                ["StartedAt"] = run.StartedAt.ToString("O"),
                ["CompletedAt"] = (object?)run.CompletedAt?.ToString("O") ?? DBNull.Value,
                ["Inputs"] = _serializer.Serialize(run.Inputs),
                ["Outputs"] = _serializer.Serialize(run.Outputs)
            };
            await _store.SaveAsync(RunsContainer, data, ct);
        }

        public async Task UpdateRunStatusAsync(string runId, string status, DateTime? completedAt = null, CancellationToken ct = default)
        {
            var run = await GetRunAsync(runId, ct);
            if (run == null) return;

            var updatedRun = new WorkflowRunRecord
            {
                Id = run.Id,
                WorkflowVersionId = run.WorkflowVersionId,
                Status = status,
                StartedAt = run.StartedAt,
                CompletedAt = completedAt,
                Inputs = run.Inputs,
                Outputs = run.Outputs
            };
            await SaveRunAsync(updatedRun, ct);
        }

        public async Task SaveMetricsAsync(IEnumerable<IRunMetric> metrics, CancellationToken ct = default)
        {
            var dataList = metrics.Select(m => new Dictionary<string, object>
            {
                ["RunId"] = m.RunId,
                ["StepName"] = m.StepName,
                ["MetricName"] = m.MetricName,
                ["Value"] = m.Value,
                ["Timestamp"] = m.Timestamp.ToString("O")
            });
            await _store.SaveBatchAsync(MetricsContainer, dataList, ct);
        }

        public async Task<IWorkflowRun?> GetRunAsync(string runId, CancellationToken ct = default)
        {
            var all = await _store.LoadAllAsync(RunsContainer, ct);
            var row = all.FirstOrDefault(d => d["Id"]?.ToString() == runId);
            if (row == null) return null;

            return MapToRun(row);
        }

        public async Task<IReadOnlyList<IRunMetric>> GetMetricsAsync(string runId, string? stepName = null, CancellationToken ct = default)
        {
            var all = await _store.LoadAllAsync(MetricsContainer, ct);
            var filtered = all.Where(d => d["RunId"]?.ToString() == runId &&
                                          (stepName == null || d["StepName"]?.ToString() == stepName));
            return filtered.Select(MapToMetric).ToList().AsReadOnly();
        }

        public async Task<IReadOnlyList<IWorkflowRun>> QueryRunsAsync(string workflowVersionId, string? status = null, int limit = 100, CancellationToken ct = default)
        {
            var all = await _store.LoadAllAsync(RunsContainer, ct);
            var query = all.Where(d => d["WorkflowVersionId"]?.ToString() == workflowVersionId);
            if (status != null)
                query = query.Where(d => d["Status"]?.ToString() == status);
            return query.OrderByDescending(d => d["StartedAt"]).Take(limit).Select(MapToRun).ToList().AsReadOnly();
        }

        private IWorkflowRun MapToRun(IDictionary<string, object> row)
        {
            return new WorkflowRunRecord
            {
                Id = row["Id"].ToString()!,
                WorkflowVersionId = row["WorkflowVersionId"].ToString()!,
                Status = row["Status"].ToString()!,
                StartedAt = DateTime.Parse(row["StartedAt"].ToString()!),
                CompletedAt = row["CompletedAt"] is DBNull ? null : DateTime.Parse(row["CompletedAt"].ToString()!),
                Inputs = _serializer.Deserialize<Dictionary<string, object>>(row["Inputs"].ToString()!),
                Outputs = _serializer.Deserialize<Dictionary<string, object>>(row["Outputs"].ToString()!)
            };
        }

        private IRunMetric MapToMetric(IDictionary<string, object> row)
        {
            return new RunMetricRecord
            {
                RunId = row["RunId"].ToString()!,
                StepName = row["StepName"].ToString()!,
                MetricName = row["MetricName"].ToString()!,
                Value = Convert.ToDouble(row["Value"]),
                Timestamp = DateTime.Parse(row["Timestamp"].ToString()!)
            };
        }
    }
}

namespace WorkflowFramework.Storage.Graph
{
    using UniversalGraphStorage;
    using WorkflowFramework.Core;

    public interface IWorkflowGraphRepository
    {
        Task AddWorkflowVersionAsync(IWorkflowVersion version, CancellationToken ct = default);
        Task AddCodeVersionAsync(ICodeVersion codeVersion, CancellationToken ct = default);
        Task AddWorkflowCodeDependencyAsync(string workflowVersionId, string codeHash, CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetDependentCodeHashesAsync(string workflowVersionId, CancellationToken ct = default);
        Task AddRunTriggerAsync(string workflowVersionId, string runId, CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetRunIdsForWorkflowVersionAsync(string workflowVersionId, CancellationToken ct = default);
        Task AddVersionLinkAsync(string previousVersionId, string newVersionId, CancellationToken ct = default);
    }

    public sealed class WorkflowGraphRepository : IWorkflowGraphRepository
    {
        private readonly IGraphReferenceManager _graph;

        public WorkflowGraphRepository(IGraphReferenceManager graph) => _graph = graph;

        private async Task<bool> ResourceExistsAsync(ResourceIdentifier resource, CancellationToken ct)
        {
            var existing = await _graph.GetResourceAsync(resource);
            return existing != null;
        }

        public async Task AddWorkflowVersionAsync(IWorkflowVersion version, CancellationToken ct = default)
        {
            var resource = new ResourceIdentifier
            {
                ResourceType = WorkflowResourceTypes.WorkflowVersion,
                Id = version.Id,
                Name = version.WorkflowId,
                Location = version.JsonContentPath
            };
            if (await ResourceExistsAsync(resource, ct)) return;

            var metadata = new Dictionary<string, object>
            {
                ["WorkflowId"] = version.WorkflowId,
                ["Version"] = version.Version,
                ["JsonPath"] = version.JsonContentPath,
                ["CreatedAt"] = version.CreatedAt.ToString("O")
            };
            await _graph.AddResourceAsync(resource, metadata, symbol: version.Id);
        }

        public async Task AddCodeVersionAsync(ICodeVersion codeVersion, CancellationToken ct = default)
        {
            var resource = new ResourceIdentifier
            {
                ResourceType = WorkflowResourceTypes.CodeVersion,
                Id = codeVersion.Id,
                Name = codeVersion.Name,
                Location = codeVersion.ContentPath
            };
            if (await ResourceExistsAsync(resource, ct)) return;

            var metadata = new Dictionary<string, object>
            {
                ["Name"] = codeVersion.Name,
                ["ContentHash"] = codeVersion.ContentHash,
                ["Language"] = codeVersion.Language,
                ["SizeBytes"] = codeVersion.SizeBytes
            };
            await _graph.AddResourceAsync(resource, metadata, symbol: codeVersion.Id);
        }

        public async Task AddWorkflowCodeDependencyAsync(string workflowVersionId, string codeHash, CancellationToken ct = default)
        {
            var source = new ResourceIdentifier { ResourceType = WorkflowResourceTypes.WorkflowVersion, Id = workflowVersionId };
            var target = new ResourceIdentifier { ResourceType = WorkflowResourceTypes.CodeVersion, Id = codeHash };

            var existing = await _graph.GetReferencesAsync(source, includeIncoming: false);
            if (existing.Any(r => r.Target.Id == codeHash && r.Description == WorkflowRelationshipTypes.DependsOnCode))
                return;

            await _graph.AddReferenceAsync(source, target, ReferenceType.DependsOn,
                description: WorkflowRelationshipTypes.DependsOnCode);
        }

        public async Task<IReadOnlyList<string>> GetDependentCodeHashesAsync(string workflowVersionId, CancellationToken ct = default)
        {
            var source = new ResourceIdentifier { ResourceType = WorkflowResourceTypes.WorkflowVersion, Id = workflowVersionId };
            var refs = await _graph.GetReferencesAsync(source, includeIncoming: false);
            return refs.Where(r => r.Description == WorkflowRelationshipTypes.DependsOnCode)
                       .Select(r => r.Target.Id)
                       .ToList();
        }

        public async Task AddRunTriggerAsync(string workflowVersionId, string runId, CancellationToken ct = default)
        {
            var source = new ResourceIdentifier { ResourceType = WorkflowResourceTypes.WorkflowVersion, Id = workflowVersionId };
            var target = new ResourceIdentifier { ResourceType = WorkflowResourceTypes.WorkflowRun, Id = runId };

            var existing = await _graph.GetReferencesAsync(source, includeIncoming: false);
            if (existing.Any(r => r.Target.Id == runId && r.Description == WorkflowRelationshipTypes.TriggersRun))
                return;

            await _graph.AddReferenceAsync(source, target, ReferenceType.Other,
                description: WorkflowRelationshipTypes.TriggersRun);
        }

        public async Task<IReadOnlyList<string>> GetRunIdsForWorkflowVersionAsync(string workflowVersionId, CancellationToken ct = default)
        {
            var source = new ResourceIdentifier { ResourceType = WorkflowResourceTypes.WorkflowVersion, Id = workflowVersionId };
            var refs = await _graph.GetReferencesAsync(source, includeIncoming: false);
            return refs.Where(r => r.Description == WorkflowRelationshipTypes.TriggersRun)
                       .Select(r => r.Target.Id)
                       .ToList();
        }

        public async Task AddVersionLinkAsync(string previousVersionId, string newVersionId, CancellationToken ct = default)
        {
            var source = new ResourceIdentifier { ResourceType = WorkflowResourceTypes.WorkflowVersion, Id = previousVersionId };
            var target = new ResourceIdentifier { ResourceType = WorkflowResourceTypes.WorkflowVersion, Id = newVersionId };

            var existing = await _graph.GetReferencesAsync(source, includeIncoming: false);
            if (existing.Any(r => r.Target.Id == newVersionId && r.Description == WorkflowRelationshipTypes.PreviousVersion))
                return;

            await _graph.AddReferenceAsync(source, target, ReferenceType.Other,
                description: WorkflowRelationshipTypes.PreviousVersion);
        }
    }
}

namespace WorkflowFramework.Services
{
    using WorkflowFramework.Core;
    using WorkflowFramework.Storage.FileSystem;
    using WorkflowFramework.Storage.Graph;
    using WorkflowFramework.Storage.Sqlite;

    public sealed class WorkflowOrchestrationService
    {
        private readonly IFileStorageService _fileStore;
        private readonly ContentAddressedCodeStore _codeStore;
        private readonly IRunDataStore _runDataStore;
        private readonly IWorkflowGraphRepository _graphRepo;

        public WorkflowOrchestrationService(
            IFileStorageService fileStore,
            ContentAddressedCodeStore codeStore,
            IRunDataStore runDataStore,
            IWorkflowGraphRepository graphRepo)
        {
            _fileStore = fileStore;
            _codeStore = codeStore;
            _runDataStore = runDataStore;
            _graphRepo = graphRepo;
        }

        public async Task<IWorkflowVersion> SaveWorkflowVersionAsync(
            string workflowId, string version, string jsonContent,
            IEnumerable<(string codeName, byte[] codeBytes, string language)> codeFiles,
            CancellationToken ct = default)
        {
            var jsonPath = Path.Combine("workflows", workflowId, version, "definition.json");
            await _fileStore.SaveContentAsync(jsonPath, Encoding.UTF8.GetBytes(jsonContent), ct);

            var codeHashes = new List<string>();
            foreach (var (name, bytes, lang) in codeFiles)
            {
                var codeVersion = await _codeStore.StoreCodeAsync(name, bytes, lang, ct);
                await _graphRepo.AddCodeVersionAsync(codeVersion, ct);
                codeHashes.Add(codeVersion.Id);
            }

            var wfVersionId = $"{workflowId}@{version}";
            var workflowVersion = new WorkflowVersionRecord
            {
                Id = wfVersionId,
                WorkflowId = workflowId,
                Version = version,
                JsonContentPath = jsonPath,
                CreatedAt = DateTime.UtcNow
            };
            await _graphRepo.AddWorkflowVersionAsync(workflowVersion, ct);

            foreach (var hash in codeHashes)
                await _graphRepo.AddWorkflowCodeDependencyAsync(wfVersionId, hash, ct);

            return workflowVersion;
        }

        public async Task<string> StartRunAsync(string workflowVersionId, Dictionary<string, object> inputs, CancellationToken ct = default)
        {
            var runId = Guid.NewGuid().ToString("N");

            var run = new WorkflowRunRecord
            {
                Id = runId,
                WorkflowVersionId = workflowVersionId,
                Status = "Pending",
                StartedAt = DateTime.UtcNow,
                Inputs = inputs,
                Outputs = new Dictionary<string, object>()
            };
            await _runDataStore.SaveRunAsync(run, ct);
            await _graphRepo.AddRunTriggerAsync(workflowVersionId, runId, ct);

            return runId;
        }

        public Task<IReadOnlyList<string>> GetDependentCodeHashesAsync(string workflowVersionId, CancellationToken ct = default)
            => _graphRepo.GetDependentCodeHashesAsync(workflowVersionId, ct);

        public Task<IReadOnlyList<IWorkflowRun>> GetRunHistoryAsync(string workflowVersionId, int limit = 20, CancellationToken ct = default)
            => _runDataStore.QueryRunsAsync(workflowVersionId, limit: limit, ct: ct);
    }
}

namespace WorkflowFramework.Examples
{
}