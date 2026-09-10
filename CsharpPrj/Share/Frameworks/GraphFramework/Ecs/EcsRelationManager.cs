
#nullable enable

using ECS.Core;
using TConfig.ECS;

namespace GraphFramework.Ecs
{
    public enum RelationDirection
    {
        From,
        To,
        Bidirectional,
        Reference,
        Composition
    }

    public readonly struct RelationEntry : IEquatable<RelationEntry>
    {
        public Entity Source { get; }

        public Entity Target { get; }

        public RelationDirection Type { get; }

        public string? Label { get; }

        public RelationEntry(Entity source, Entity target, RelationDirection type, string? label = null)
        {
            Source = source;
            Target = target;
            Type = type;
            Label = label;
        }

        public bool Equals(RelationEntry other)
            => Source.Equals(other.Source) && Target.Equals(other.Target)
               && Type == other.Type && Label == other.Label;

        public override bool Equals(object? obj)
            => obj is RelationEntry other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Source, Target, Type, Label);

        public override string ToString()
            => $"{Source} --[{Type}]--> {Target}" + (Label != null ? $" ({Label})" : "");
    }

    public class EcsRelationManager : IDisposable
    {
        private readonly EntityManager _entityManager;
        private readonly HashSet<RelationEntry> _relations = new();
        private readonly Dictionary<Entity, HashSet<RelationEntry>> _fromIndex = new();
        private readonly Dictionary<Entity, HashSet<RelationEntry>> _toIndex = new();
        private readonly object _lock = new();
        private bool _disposed;

        public EcsRelationManager(EntityManager entityManager)
        {
            _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        }

        public void AddRelation(Entity source, Entity target, RelationDirection type, string? label = null)
        {
            var entry = new RelationEntry(source, target, type, label);

            lock (_lock)
            {
                if (!_relations.Add(entry))
                    return;

                if (!_fromIndex.TryGetValue(source, out var fromSet))
                {
                    fromSet = new HashSet<RelationEntry>();
                    _fromIndex[source] = fromSet;
                }
                fromSet.Add(entry);

                if (!_toIndex.TryGetValue(target, out var toSet))
                {
                    toSet = new HashSet<RelationEntry>();
                    _toIndex[target] = toSet;
                }
                toSet.Add(entry);
            }
        }

        public bool RemoveRelation(Entity source, Entity target, RelationDirection type, string? label = null)
        {
            var entry = new RelationEntry(source, target, type, label);

            lock (_lock)
            {
                if (!_relations.Remove(entry))
                    return false;

                if (_fromIndex.TryGetValue(source, out var fromSet))
                {
                    fromSet.Remove(entry);
                    if (fromSet.Count == 0)
                        _fromIndex.Remove(source);
                }

                if (_toIndex.TryGetValue(target, out var toSet))
                {
                    toSet.Remove(entry);
                    if (toSet.Count == 0)
                        _toIndex.Remove(target);
                }

                return true;
            }
        }

        public void RemoveAllRelations(Entity entity)
        {
            lock (_lock)
            {
                if (_fromIndex.TryGetValue(entity, out var fromSet))
                {
                    foreach (var entry in fromSet)
                    {
                        _relations.Remove(entry);
                        if (_toIndex.TryGetValue(entry.Target, out var toSetForTarget))
                        {
                            toSetForTarget.Remove(entry);
                            if (toSetForTarget.Count == 0)
                                _toIndex.Remove(entry.Target);
                        }
                    }
                    _fromIndex.Remove(entity);
                }

                if (_toIndex.TryGetValue(entity, out var toSet))
                {
                    foreach (var entry in toSet)
                    {
                        _relations.Remove(entry);
                        if (_fromIndex.TryGetValue(entry.Source, out var fromSet2))
                        {
                            fromSet2.Remove(entry);
                            if (fromSet2.Count == 0)
                                _fromIndex.Remove(entry.Source);
                        }
                    }
                    _toIndex.Remove(entity);
                }
            }
        }

        public IReadOnlyList<RelationEntry> GetRelationsFrom(Entity source, RelationDirection? type = null)
        {
            lock (_lock)
            {
                if (!_fromIndex.TryGetValue(source, out var fromSet))
                    return Array.Empty<RelationEntry>();

                if (type == null)
                    return fromSet.ToList();

                return fromSet.Where(r => r.Type == type.Value).ToList();
            }
        }

        public IReadOnlyList<RelationEntry> GetRelationsTo(Entity target, RelationDirection? type = null)
        {
            lock (_lock)
            {
                if (!_toIndex.TryGetValue(target, out var toSet))
                    return Array.Empty<RelationEntry>();

                if (type == null)
                    return toSet.ToList();

                return toSet.Where(r => r.Type == type.Value).ToList();
            }
        }

        public IReadOnlyList<RelationEntry> GetAllRelations(Entity entity)
        {
            lock (_lock)
            {
                var result = new List<RelationEntry>();

                if (_fromIndex.TryGetValue(entity, out var fromSet))
                    result.AddRange(fromSet);

                if (_toIndex.TryGetValue(entity, out var toSet))
                    result.AddRange(toSet);

                return result;
            }
        }

        public IReadOnlyList<Entity> GetRelatedEntities(Entity source, RelationDirection type)
        {
            lock (_lock)
            {
                if (!_fromIndex.TryGetValue(source, out var fromSet))
                    return Array.Empty<Entity>();

                return fromSet
                    .Where(r => r.Type == type)
                    .Select(r => r.Target)
                    .Distinct()
                    .ToList();
            }
        }

        public bool HasRelation(Entity source, Entity target, RelationDirection? type = null, string? label = null)
        {
            lock (_lock)
            {
                return _relations.Any(r =>
                    r.Source.Equals(source) &&
                    r.Target.Equals(target) &&
                    (type == null || r.Type == type.Value) &&
                    (label == null || r.Label == label));
            }
        }

        public int GetRelationCount(Entity entity)
        {
            lock (_lock)
            {
                int count = 0;
                if (_fromIndex.TryGetValue(entity, out var fromSet))
                    count += fromSet.Count;
                if (_toIndex.TryGetValue(entity, out var toSet))
                    count += toSet.Count;
                return count;
            }
        }

        public IReadOnlyList<RelationEntry> GetAllRelations()
        {
            lock (_lock)
            {
                return _relations.ToList();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _relations.Clear();
                _fromIndex.Clear();
                _toIndex.Clear();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Clear();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}


