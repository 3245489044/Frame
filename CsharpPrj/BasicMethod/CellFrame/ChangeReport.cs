#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CellChangeTracker
{
    public class ChangeSummary
    {
        public int Added { get; set; }
        public int Removed { get; set; }
        public int Modified { get; set; }
        public int Unchanged { get; set; }
        public int Total => Added + Removed + Modified + Unchanged;
    }

    public class ChangeReport
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public ChangeSummary Summary { get; set; } = new ChangeSummary();
        public List<ReferenceDetail> AddedReferences { get; set; } = new List<ReferenceDetail>();
        public List<ReferenceDetail> RemovedReferences { get; set; } = new List<ReferenceDetail>();
        public List<ModifiedDetail> ModifiedReferences { get; set; } = new List<ModifiedDetail>();
        public List<string> UnchangedReferences { get; set; } = new List<string>();

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("引用变化分析报告");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("【时间范围】");
            sb.AppendLine($"T0: {From:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"T1: {To:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("【变化摘要】");
            sb.AppendLine("┌─────────────┬───────┐");
            sb.AppendLine("│   变化类型  │ 数量  │");
            sb.AppendLine("├─────────────┼───────┤");
            sb.AppendLine($"│   新增引用  │ {Summary.Added,4}   │");
            sb.AppendLine($"│   删除引用  │ {Summary.Removed,4}   │");
            sb.AppendLine($"│   修改引用  │ {Summary.Modified,4}   │");
            sb.AppendLine($"│   未变引用  │ {Summary.Unchanged,4}   │");
            sb.AppendLine($"│   总计      │ {Summary.Total,4}   │");
            sb.AppendLine("└─────────────┴───────┘");
            return sb.ToString();
        }
    }

    public class ReferenceDetail
    {
        public string ReferenceId { get; set; }
        public string SourceModule { get; set; }
        public string TargetModule { get; set; }
        public string RelationType { get; set; }
        public string Properties { get; set; }
    }

    public class ModifiedDetail
    {
        public string ReferenceId { get; set; }
        public string PropertyName { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
        public string ChangeAmount { get; set; }
    }
}
namespace CellChangeTracker
{
    public interface IReferenceChangeTracker
    {
        DiscreteChange GetChanges(DateTime from, DateTime to);
        List<ChangeRecord> GetReferenceHistory(string referenceId);
        DiscreteChange GetModuleChanges(string moduleId, DateTime from, DateTime to);
        List<Reference> QueryAddedReferences(DateTime from, DateTime to);
        List<Reference> QueryRemovedReferences(DateTime from, DateTime to);
        List<ModifiedReference> QueryModifiedReferences(DateTime from, DateTime to);
        DiscreteChange CompareStates(DateTime t0, DateTime t1);
        ChangeReport GenerateChangeReport(DateTime from, DateTime to);
        
        Task<DiscreteChange> GetChangesAsync(DateTime from, DateTime to);
        Task<List<ChangeRecord>> GetReferenceHistoryAsync(string referenceId);
        Task<DiscreteChange> GetModuleChangesAsync(string moduleId, DateTime from, DateTime to);
        Task<List<Reference>> QueryAddedReferencesAsync(DateTime from, DateTime to);
        Task<List<Reference>> QueryRemovedReferencesAsync(DateTime from, DateTime to);
        Task<List<ModifiedReference>> QueryModifiedReferencesAsync(DateTime from, DateTime to);
        Task<DiscreteChange> CompareStatesAsync(DateTime t0, DateTime t1);
        Task<ChangeReport> GenerateChangeReportAsync(DateTime from, DateTime to);
    }

    public class DiscreteChange
    {
        public HashSet<Reference> Added { get; set; } = new HashSet<Reference>();
        public HashSet<Reference> Removed { get; set; } = new HashSet<Reference>();
        public HashSet<Reference> Unchanged { get; set; } = new HashSet<Reference>();
        public HashSet<ModifiedReference> Modified { get; set; } = new HashSet<ModifiedReference>();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string OperationSource { get; set; } = string.Empty;
    }

    public class ModifiedReference
    {
        public Reference Reference { get; set; } = null!;
        public Dictionary<string, object> OldProperties { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> NewProperties { get; set; } = new Dictionary<string, object>();
    }

    public class Reference : IEquatable<Reference>
    {
        public string SourceModuleId { get; set; } = string.Empty;
        public string TargetModuleId { get; set; } = string.Empty;
        public RelationType RelationType { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

        public bool Equals(Reference? other)
        {
            if (other is null) return false;
            return SourceModuleId == other.SourceModuleId &&
                   TargetModuleId == other.TargetModuleId &&
                   RelationType == other.RelationType;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as Reference);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SourceModuleId, TargetModuleId, RelationType);
        }
    }

    public class ChangeRecord
    {
        public Guid Id { get; set; }
        public string ReferenceId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Operation { get; set; } = string.Empty;
        public Dictionary<string, object> BeforeState { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> AfterState { get; set; } = new Dictionary<string, object>();
    }
}
namespace CellChangeTracker
{
    public class DiscreteChangeCalculator<T> : IDiscreteChangeCalculator<T>
    {
        private readonly IEqualityComparer<T> _comparer;
        private readonly ILogger<DiscreteChangeCalculator<T>>? _logger;

        public DiscreteChangeCalculator() : this(EqualityComparer<T>.Default, null) { }

        public DiscreteChangeCalculator(IEqualityComparer<T> comparer) : this(comparer, null) { }

        public DiscreteChangeCalculator(IEqualityComparer<T> comparer, ILogger<DiscreteChangeCalculator<T>>? logger)
        {
            _comparer = comparer;
            _logger = logger;
        }

        public ChangeVector<T> CalculateSetDifference(IEnumerable<T> before, IEnumerable<T> after)
        {
            _logger?.LogDebug("计算集合差");
            var beforeSet = new HashSet<T>(before, _comparer);
            var afterSet = new HashSet<T>(after, _comparer);

            return new ChangeVector<T>
            {
                Added = afterSet.Except(beforeSet).ToList(),
                Removed = beforeSet.Except(afterSet).ToList(),
                Unchanged = beforeSet.Intersect(afterSet).ToList()
            };
        }

        public List<T> CalculateSetIntersection(IEnumerable<T> set1, IEnumerable<T> set2)
        {
            var hashSet1 = new HashSet<T>(set1, _comparer);
            return set2.Where(hashSet1.Contains).ToList();
        }

        public List<T> CalculateSetUnion(IEnumerable<T> set1, IEnumerable<T> set2)
        {
            var hashSet = new HashSet<T>(_comparer);
            foreach (var item in set1) hashSet.Add(item);
            foreach (var item in set2) hashSet.Add(item);
            return hashSet.ToList();
        }

        public List<ModifiedItem<T>> CalculateModifiedItems(IEnumerable<T> before, IEnumerable<T> after, 
            Func<T, T, bool> isModified)
        {
            var beforeDict = before.ToDictionary(item => item, _comparer);
            var afterDict = after.ToDictionary(item => item, _comparer);
            
            var result = new List<ModifiedItem<T>>();
            
            foreach (var key in beforeDict.Keys.Intersect(afterDict.Keys))
            {
                if (isModified(beforeDict[key], afterDict[key]))
                {
                    result.Add(new ModifiedItem<T>
                    {
                        OldValue = beforeDict[key],
                        NewValue = afterDict[key]
                    });
                }
            }
            
            return result;
        }

        public ChangeVector<T> CompareSnapshots(StateSnapshot<T> before, StateSnapshot<T> after,
            Func<T, T, bool> isModified = null)
        {
            _logger?.LogDebug($"比较快照: {before.Timestamp} -> {after.Timestamp}");
            var vector = CalculateSetDifference(before.Data, after.Data);
            
            if (isModified != null)
            {
                vector.Modified = CalculateModifiedItems(before.Data, after.Data, isModified);
            }
            
            return vector;
        }

        public ChangeReport<T> GenerateChangeReport(StateSnapshot<T> before, StateSnapshot<T> after,
            Func<T, T, bool> isModified = null)
        {
            var changes = CompareSnapshots(before, after, isModified);
            
            return new ChangeReport<T>
            {
                FromTimestamp = before.Timestamp,
                ToTimestamp = after.Timestamp,
                ChangeVector = changes,
                Summary = new ChangeSummary
                {
                    Added = changes.Added.Count,
                    Removed = changes.Removed.Count,
                    Modified = changes.Modified?.Count ?? 0,
                    Unchanged = changes.Unchanged.Count
                }
            };
        }

        public ChangeReport<T> GenerateChangeReport(IEnumerable<T> before, IEnumerable<T> after,
            Func<T, T, bool> isModified = null)
        {
            return GenerateChangeReport(
                new StateSnapshot<T> { Timestamp = DateTime.MinValue, Data = before.ToList() },
                new StateSnapshot<T> { Timestamp = DateTime.Now, Data = after.ToList() },
                isModified
            );
        }

        public DiscreteSet<T> CreateDiscreteSet(params T[] items)
        {
            return new DiscreteSet<T>(items, _comparer);
        }

        public StateSnapshot<T> CreateSnapshot(IEnumerable<T> data)
        {
            return new StateSnapshot<T>
            {
                Timestamp = DateTime.Now,
                Data = data.ToList()
            };
        }
    }

    public interface IDiscreteChangeCalculator<T>
    {
        ChangeVector<T> CalculateSetDifference(IEnumerable<T> before, IEnumerable<T> after);
        List<T> CalculateSetIntersection(IEnumerable<T> set1, IEnumerable<T> set2);
        List<T> CalculateSetUnion(IEnumerable<T> set1, IEnumerable<T> set2);
        List<ModifiedItem<T>> CalculateModifiedItems(IEnumerable<T> before, IEnumerable<T> after,
            Func<T, T, bool> isModified);
        ChangeVector<T> CompareSnapshots(StateSnapshot<T> before, StateSnapshot<T> after,
            Func<T, T, bool> isModified = null);
        ChangeReport<T> GenerateChangeReport(StateSnapshot<T> before, StateSnapshot<T> after,
            Func<T, T, bool> isModified = null);
        DiscreteSet<T> CreateDiscreteSet(params T[] items);
        StateSnapshot<T> CreateSnapshot(IEnumerable<T> data);
    }

    public class DiscreteSet<T>
    {
        private readonly HashSet<T> _items;

        public DiscreteSet(IEnumerable<T> items, IEqualityComparer<T> comparer)
        {
            _items = new HashSet<T>(items, comparer);
        }

        public int Count => _items.Count;
        
        public bool Contains(T item) => _items.Contains(item);
        
        public bool Add(T item) => _items.Add(item);
        
        public bool Remove(T item) => _items.Remove(item);
        
        public IEnumerable<T> Items => _items;

        public DiscreteSet<T> Union(DiscreteSet<T> other)
        {
            var result = new HashSet<T>(_items, _items.Comparer);
            foreach (var item in other._items) result.Add(item);
            return new DiscreteSet<T>(result, _items.Comparer);
        }

        public DiscreteSet<T> Intersect(DiscreteSet<T> other)
        {
            var result = _items.Where(other._items.Contains).ToList();
            return new DiscreteSet<T>(result, _items.Comparer);
        }

        public DiscreteSet<T> Except(DiscreteSet<T> other)
        {
            var result = _items.Where(item => !other._items.Contains(item)).ToList();
            return new DiscreteSet<T>(result, _items.Comparer);
        }
    }

    public class StateSnapshot<T>
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public List<T> Data { get; set; } = new List<T>();
    }

    public class ChangeVector<T>
    {
        public List<T> Added { get; set; } = new List<T>();
        public List<T> Removed { get; set; } = new List<T>();
        public List<T> Unchanged { get; set; } = new List<T>();
        public List<ModifiedItem<T>> Modified { get; set; } = new List<ModifiedItem<T>>();
    }

    public class ModifiedItem<T>
    {
        public T OldValue { get; set; } = default!;
        public T NewValue { get; set; } = default!;
    }

    public class ChangeReport<T>
    {
        public DateTime FromTimestamp { get; set; }
        public DateTime ToTimestamp { get; set; }
        public ChangeVector<T> ChangeVector { get; set; } = null!;
        public ChangeSummary Summary { get; set; } = null!;
    }

    public class ReferenceChangeTracker : IReferenceChangeTracker
    {
        private readonly List<ChangeRecord> _changeRecords = new List<ChangeRecord>();
        private readonly Dictionary<string, Reference> _currentReferences = new Dictionary<string, Reference>();
        private readonly ILogger<ReferenceChangeTracker>? _logger;

        public ReferenceChangeTracker(ILogger<ReferenceChangeTracker>? logger = null)
        {
            _logger = logger;
        }

        public DiscreteChange GetChanges(DateTime from, DateTime to)
        {
            var changes = _changeRecords.Where(r => r.Timestamp >= from && r.Timestamp <= to).ToList();
            return BuildDiscreteChange(changes);
        }

        public List<ChangeRecord> GetReferenceHistory(string referenceId)
        {
            return _changeRecords.Where(r => r.ReferenceId == referenceId).OrderBy(r => r.Timestamp).ToList();
        }

        public DiscreteChange GetModuleChanges(string moduleId, DateTime from, DateTime to)
        {
            var changes = _changeRecords.Where(r => 
                r.Timestamp >= from && r.Timestamp <= to &&
                (r.BeforeState.ContainsKey("ModuleId") && r.BeforeState["ModuleId"]?.ToString() == moduleId ||
                 r.AfterState.ContainsKey("ModuleId") && r.AfterState["ModuleId"]?.ToString() == moduleId)).ToList();
            return BuildDiscreteChange(changes);
        }

        public List<Reference> QueryAddedReferences(DateTime from, DateTime to)
        {
            return GetChanges(from, to).Added.ToList();
        }

        public List<Reference> QueryRemovedReferences(DateTime from, DateTime to)
        {
            return GetChanges(from, to).Removed.ToList();
        }

        public List<ModifiedReference> QueryModifiedReferences(DateTime from, DateTime to)
        {
            return GetChanges(from, to).Modified.ToList();
        }

        public DiscreteChange CompareStates(DateTime t0, DateTime t1)
        {
            return GetChanges(t0, t1);
        }

        public ChangeReport GenerateChangeReport(DateTime from, DateTime to)
        {
            var changes = GetChanges(from, to);
            return new ChangeReport
            {
                From = from,
                To = to,
                Summary = new ChangeSummary
                {
                    Added = changes.Added.Count,
                    Removed = changes.Removed.Count,
                    Modified = changes.Modified.Count,
                    Unchanged = changes.Unchanged.Count
                },
                AddedReferences = changes.Added.Select(r => new ReferenceDetail
                {
                    ReferenceId = $"{r.SourceModuleId}_{r.TargetModuleId}_{r.RelationType}",
                    SourceModule = r.SourceModuleId,
                    TargetModule = r.TargetModuleId,
                    RelationType = r.RelationType.ToString(),
                    Properties = JsonConvert.SerializeObject(r.Properties)
                }).ToList(),
                RemovedReferences = changes.Removed.Select(r => new ReferenceDetail
                {
                    ReferenceId = $"{r.SourceModuleId}_{r.TargetModuleId}_{r.RelationType}",
                    SourceModule = r.SourceModuleId,
                    TargetModule = r.TargetModuleId,
                    RelationType = r.RelationType.ToString(),
                    Properties = JsonConvert.SerializeObject(r.Properties)
                }).ToList(),
                ModifiedReferences = changes.Modified.Select(m => new ModifiedDetail
                {
                    ReferenceId = $"{m.Reference.SourceModuleId}_{m.Reference.TargetModuleId}_{m.Reference.RelationType}",
                    PropertyName = m.OldProperties.Keys.FirstOrDefault() ?? string.Empty,
                    OldValue = m.OldProperties.Values.FirstOrDefault(),
                    NewValue = m.NewProperties.Values.FirstOrDefault()
                }).ToList()
            };
        }

        private DiscreteChange BuildDiscreteChange(List<ChangeRecord> changes)
        {
            var result = new DiscreteChange();
            
            foreach (var record in changes)
            {
                var reference = new Reference
                {
                    SourceModuleId = record.AfterState.TryGetValue("SourceModuleId", out var src) ? src.ToString() ?? string.Empty : string.Empty,
                    TargetModuleId = record.AfterState.TryGetValue("TargetModuleId", out var tgt) ? tgt.ToString() ?? string.Empty : string.Empty,
                    RelationType = Enum.TryParse<RelationType>(record.AfterState.TryGetValue("RelationType", out var rt) ? rt.ToString() : string.Empty, out var type) ? type : RelationType.Dependency
                };

                switch (record.Operation.ToLower())
                {
                    case "create":
                    case "add":
                        result.Added.Add(reference);
                        break;
                    case "delete":
                    case "remove":
                        result.Removed.Add(reference);
                        break;
                    case "update":
                    case "modify":
                        result.Modified.Add(new ModifiedReference { Reference = reference, OldProperties = record.BeforeState, NewProperties = record.AfterState });
                        break;
                    default:
                        result.Unchanged.Add(reference);
                        break;
                }
            }

            return result;
        }

        public Task<DiscreteChange> GetChangesAsync(DateTime from, DateTime to)
        {
            return Task.FromResult(GetChanges(from, to));
        }

        public Task<List<ChangeRecord>> GetReferenceHistoryAsync(string referenceId)
        {
            return Task.FromResult(GetReferenceHistory(referenceId));
        }

        public Task<DiscreteChange> GetModuleChangesAsync(string moduleId, DateTime from, DateTime to)
        {
            return Task.FromResult(GetModuleChanges(moduleId, from, to));
        }

        public Task<List<Reference>> QueryAddedReferencesAsync(DateTime from, DateTime to)
        {
            return Task.FromResult(QueryAddedReferences(from, to));
        }

        public Task<List<Reference>> QueryRemovedReferencesAsync(DateTime from, DateTime to)
        {
            return Task.FromResult(QueryRemovedReferences(from, to));
        }

        public Task<List<ModifiedReference>> QueryModifiedReferencesAsync(DateTime from, DateTime to)
        {
            return Task.FromResult(QueryModifiedReferences(from, to));
        }

        public Task<DiscreteChange> CompareStatesAsync(DateTime t0, DateTime t1)
        {
            return Task.FromResult(CompareStates(t0, t1));
        }

        public Task<ChangeReport> GenerateChangeReportAsync(DateTime from, DateTime to)
        {
            return Task.FromResult(GenerateChangeReport(from, to));
        }
    }
}