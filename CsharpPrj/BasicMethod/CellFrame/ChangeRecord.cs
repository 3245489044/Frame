using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CellReferenceManagement
{
    public class ReferenceManagementLayer : IReferenceManagementLayer
    {
        private readonly Dictionary<string, ReferenceRelation> _references = new Dictionary<string, ReferenceRelation>();
        private readonly List<ChangeRecord> _changeRecords = new List<ChangeRecord>();
        private readonly IReferenceStorage? _storage;
        private readonly ILogger<ReferenceManagementLayer>? _logger;

        public ReferenceManagementLayer(IReferenceStorage? storage = null, ILogger<ReferenceManagementLayer>? logger = null)
        {
            _storage = storage;
            _logger = logger;
        }

        public Guid CreateReference(string source, string target, RelationType type)
        {
            var id = Guid.NewGuid();
            var key = $"{source}_{target}_{type}";
            
            if (_references.ContainsKey(key))
                return _references[key].Id;

            var relation = new ReferenceRelation
            {
                Id = id,
                SourceModuleId = source,
                TargetModuleId = target,
                RelationType = type,
                Strength = 1.0,
                CreatedAt = DateTime.Now
            };

            _references[key] = relation;
            _storage?.SaveReference(relation);
            _logger?.LogInformation($"创建引用关系: {source} -> {target} ({type})");

            return id;
        }

        public bool RemoveReference(string source, string target, RelationType type)
        {
            var key = $"{source}_{target}_{type}";
            if (_references.TryGetValue(key, out var relation))
            {
                _references.Remove(key);
                _storage?.DeleteReference(relation.Id);
                _logger?.LogInformation($"删除引用关系: {source} -> {target} ({type})");
                return true;
            }
            return false;
        }

        public List<ReferenceRelation> GetReferences(string resourceId, Direction direction)
        {
            var result = _references.Values.Where(r =>
                direction == Direction.Both && (r.SourceModuleId == resourceId || r.TargetModuleId == resourceId) ||
                direction == Direction.Incoming && r.TargetModuleId == resourceId ||
                direction == Direction.Outgoing && r.SourceModuleId == resourceId
            ).ToList();
            
            _logger?.LogInformation($"获取引用列表成功: {resourceId}，共 {result.Count} 个引用");
            return result;
        }

        public List<ReferenceRelation> GetDependencies(string resourceId, int depth)
        {
            var result = new List<ReferenceRelation>();
            var visited = new HashSet<string>();
            GetDependenciesRecursive(resourceId, depth, 0, visited, result);
            _logger?.LogInformation($"获取依赖关系成功: {resourceId}，深度: {depth}，共 {result.Count} 个依赖");
            return result;
        }

        private void GetDependenciesRecursive(string resourceId, int maxDepth, int currentDepth, HashSet<string> visited, List<ReferenceRelation> result)
        {
            if (currentDepth >= maxDepth || visited.Contains(resourceId))
                return;

            visited.Add(resourceId);
            var dependencies = _references.Values.Where(r => r.SourceModuleId == resourceId).ToList();
            
            foreach (var dep in dependencies)
            {
                result.Add(dep);
                GetDependenciesRecursive(dep.TargetModuleId, maxDepth, currentDepth + 1, visited, result);
            }
        }

        public Guid RecordChange(string moduleId, OperationType operation, Dictionary<string, object>? before, Dictionary<string, object>? after, string? reason = null)
        {
            var record = new ChangeRecord
            {
                Id = Guid.NewGuid(),
                ModuleId = moduleId,
                ModuleType = ParseModuleType(moduleId),
                Operation = operation,
                BeforeState = before ?? new Dictionary<string, object>(),
                AfterState = after ?? new Dictionary<string, object>(),
                Timestamp = DateTime.Now,
                Operator = "system",
                Reason = reason ?? string.Empty
            };

            _changeRecords.Add(record);
            _storage?.SaveChangeRecord(record);
            _logger?.LogInformation($"记录变更: {moduleId} - {operation}");

            return record.Id;
        }

        public ChangeRecord? GetChangeRecord(Guid recordId)
        {
            return _changeRecords.FirstOrDefault(r => r.Id == recordId);
        }

        public List<ChangeRecord> QueryChanges(ChangeFilter filter)
        {
            return _changeRecords.Where(r =>
                (string.IsNullOrEmpty(filter.ModuleId) || r.ModuleId == filter.ModuleId) &&
                (!filter.ModuleType.HasValue || r.ModuleType == filter.ModuleType.Value) &&
                (!filter.OperationType.HasValue || r.Operation == filter.OperationType.Value) &&
                (!filter.From.HasValue || r.Timestamp >= filter.From.Value) &&
                (!filter.To.HasValue || r.Timestamp <= filter.To.Value) &&
                (string.IsNullOrEmpty(filter.Operator) || r.Operator == filter.Operator)
            ).ToList();
        }

        public Stream ExportChanges(DateTime from, DateTime to)
        {
            var changes = QueryChanges(new ChangeFilter { From = from, To = to });
            var json = JsonConvert.SerializeObject(changes, Formatting.Indented);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return stream;
        }

        public double CalculateImprovement(string moduleId)
        {
            var changes = _changeRecords.Where(r => r.ModuleId == moduleId).ToList();
            if (changes.Count == 0)
            {
                _logger?.LogInformation($"计算改进程度: {moduleId} = 0.0");
                return 0.0;
            }

            var positiveChanges = changes.Count(c => c.Operation == OperationType.Create || c.Operation == OperationType.Update);
            var improvement = (double)positiveChanges / changes.Count;

            _logger?.LogInformation($"计算改进程度: {moduleId} = {improvement:P2}");
            return improvement;
        }

        public DependencyGraph AnalyzeDependencyGraph()
        {
            var graph = new DependencyGraph();
            graph.Relations = _references.Values.ToList();
            
            foreach (var relation in _references.Values)
            {
                graph.AddNode(relation.SourceModuleId);
                graph.AddNode(relation.TargetModuleId);
                graph.AddEdge(relation.SourceModuleId, relation.TargetModuleId);
            }

            return graph;
        }

        public List<CircularDependency> DetectCircularDependencies()
        {
            var result = new List<CircularDependency>();
            var visited = new HashSet<string>();

            foreach (var moduleId in _references.Values.Select(r => r.SourceModuleId).Distinct())
            {
                if (!visited.Contains(moduleId))
                {
                    var path = new List<string>();
                    HasCycle(moduleId, visited, new HashSet<string>(), path, result);
                }
            }

            return result;
        }

        private bool HasCycle(string current, HashSet<string> visited, HashSet<string> inStack, List<string> path, List<CircularDependency> cycles)
        {
            if (inStack.Contains(current))
            {
                var cycleStart = path.IndexOf(current);
                if (cycleStart != -1)
                {
                    cycles.Add(new CircularDependency
                    {
                        ModuleIds = path.Skip(cycleStart).ToList(),
                        Depth = path.Count - cycleStart
                    });
                }
                return true;
            }

            if (visited.Contains(current))
                return false;

            visited.Add(current);
            inStack.Add(current);
            path.Add(current);

            var dependencies = _references.Values.Where(r => r.SourceModuleId == current).Select(r => r.TargetModuleId);
            foreach (var dep in dependencies)
            {
                if (HasCycle(dep, visited, inStack, path, cycles))
                    return true;
            }

            inStack.Remove(current);
            path.RemoveAt(path.Count - 1);
            return false;
        }

        private ModuleType ParseModuleType(string moduleId)
        {
            if (moduleId.Contains("sql", StringComparison.OrdinalIgnoreCase))
                return ModuleType.Sql;
            if (moduleId.Contains("code", StringComparison.OrdinalIgnoreCase))
                return ModuleType.Code;
            if (moduleId.Contains("config", StringComparison.OrdinalIgnoreCase))
                return ModuleType.Config;
            return ModuleType.Code;
        }

        public Task<Guid> CreateReferenceAsync(string source, string target, RelationType type)
        {
            return Task.FromResult(CreateReference(source, target, type));
        }

        public Task<bool> RemoveReferenceAsync(string source, string target, RelationType type)
        {
            return Task.FromResult(RemoveReference(source, target, type));
        }

        public Task<List<ReferenceRelation>> GetReferencesAsync(string resourceId, Direction direction)
        {
            return Task.FromResult(GetReferences(resourceId, direction));
        }

        public Task<List<ReferenceRelation>> GetDependenciesAsync(string resourceId, int depth = 5)
        {
            return Task.FromResult(GetDependencies(resourceId, depth));
        }

        public Task<Guid> RecordChangeAsync(string moduleId, OperationType operation, Dictionary<string, object>? before, Dictionary<string, object>? after, string? reason = null)
        {
            return Task.FromResult(RecordChange(moduleId, operation, before, after, reason));
        }

        public Task<ChangeRecord?> GetChangeRecordAsync(Guid recordId)
        {
            return Task.FromResult(GetChangeRecord(recordId));
        }

        public Task<List<ChangeRecord>> QueryChangesAsync(ChangeFilter filter)
        {
            return Task.FromResult(QueryChanges(filter));
        }

        public Task<Stream> ExportChangesAsync(DateTime from, DateTime to)
        {
            return Task.FromResult(ExportChanges(from, to));
        }

        public Task<double> CalculateImprovementAsync(string moduleId)
        {
            return Task.FromResult(CalculateImprovement(moduleId));
        }

        public Task<DependencyGraph> AnalyzeDependencyGraphAsync()
        {
            return Task.FromResult(AnalyzeDependencyGraph());
        }

        public Task<List<CircularDependency>> DetectCircularDependenciesAsync()
        {
            return Task.FromResult(DetectCircularDependencies());
        }
    }

    public interface IReferenceStorage
    {
        void SaveReference(ReferenceRelation relation);
        void DeleteReference(Guid id);
        void SaveChangeRecord(ChangeRecord record);
        List<ReferenceRelation> LoadReferences();
        List<ChangeRecord> LoadChangeRecords();
    }
}
namespace CellReferenceManagement
{
    public interface IReferenceManagementLayer
    {
        Guid CreateReference(string source, string target, RelationType type);
        bool RemoveReference(string source, string target, RelationType type);
        List<ReferenceRelation> GetReferences(string resourceId, Direction direction);
        List<ReferenceRelation> GetDependencies(string resourceId, int depth);
        Guid RecordChange(string moduleId, OperationType operation, Dictionary<string, object>? before, Dictionary<string, object>? after, string? reason = null);
        ChangeRecord? GetChangeRecord(Guid recordId);
        List<ChangeRecord> QueryChanges(ChangeFilter filter);
        Stream ExportChanges(DateTime from, DateTime to);
        double CalculateImprovement(string moduleId);
        DependencyGraph AnalyzeDependencyGraph();
        List<CircularDependency> DetectCircularDependencies();
        
        Task<Guid> CreateReferenceAsync(string source, string target, RelationType type);
        Task<bool> RemoveReferenceAsync(string source, string target, RelationType type);
        Task<List<ReferenceRelation>> GetReferencesAsync(string resourceId, Direction direction);
        Task<List<ReferenceRelation>> GetDependenciesAsync(string resourceId, int depth = 5);
        Task<Guid> RecordChangeAsync(string moduleId, OperationType operation, Dictionary<string, object>? before, Dictionary<string, object>? after, string? reason = null);
        Task<ChangeRecord?> GetChangeRecordAsync(Guid recordId);
        Task<List<ChangeRecord>> QueryChangesAsync(ChangeFilter filter);
        Task<Stream> ExportChangesAsync(DateTime from, DateTime to);
        Task<double> CalculateImprovementAsync(string moduleId);
        Task<DependencyGraph> AnalyzeDependencyGraphAsync();
        Task<List<CircularDependency>> DetectCircularDependenciesAsync();
    }

    public class ChangeFilter
    {
        public string? ModuleId { get; set; }
        public ModuleType? ModuleType { get; set; }
        public OperationType? OperationType { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? Operator { get; set; }
    }
}
namespace CellReferenceManagement
{
    public enum ModuleType
    {
        Sql,
        Code,
        Config
    }

    public class ChangeRecord
    {
        public Guid Id { get; set; }
        public string ModuleId { get; set; }
        public ModuleType ModuleType { get; set; }
        public OperationType Operation { get; set; }
        public Guid ReferenceId { get; set; }
        public Dictionary<string, object> BeforeState { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> AfterState { get; set; } = new Dictionary<string, object>();
        public DateTime Timestamp { get; set; }
        public string Operator { get; set; }
        public string Reason { get; set; }
    }
}