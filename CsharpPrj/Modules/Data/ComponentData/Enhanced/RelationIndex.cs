using ECS.Core;
using TConfig.ECS;
namespace Modules.ComponentData.Enhanced
{
    internal class RelationIndex<TKey>
    {
        private readonly Dictionary<Entity, Dictionary<TKey, HashSet<Entity>>> _fwd = new();
        private readonly Dictionary<Entity, Dictionary<TKey, HashSet<Entity>>> _rev = new();

        public void Add(Entity src, Entity tgt, TKey type)
        {
            GetOrCreate(_fwd, src, type).Add(tgt);
            GetOrCreate(_rev, tgt, type).Add(src);
        }

        public bool Remove(Entity src, Entity tgt, TKey type)
        {
            bool removed = false;
            if (_fwd.TryGetValue(src, out var srcDict) && srcDict.TryGetValue(type, out var srcSet) && srcSet.Remove(tgt))
            {
                removed = true;
                if (srcSet.Count == 0) { srcDict.Remove(type); if (srcDict.Count == 0) _fwd.Remove(src); }
            }
            if (_rev.TryGetValue(tgt, out var tgtDict) && tgtDict.TryGetValue(type, out var tgtSet) && tgtSet.Remove(src))
            {
                removed = true;
                if (tgtSet.Count == 0) { tgtDict.Remove(type); if (tgtDict.Count == 0) _rev.Remove(tgt); }
            }
            return removed;
        }

        public void RemoveAll(Entity e)
        {
            var removed = new List<(Entity, Entity, TKey)>();
            if (_fwd.TryGetValue(e, out var fwdDict))
                foreach (var kv in fwdDict)
                    removed.AddRange(kv.Value.Select(tgt => (e, tgt, kv.Key)));
            if (_rev.TryGetValue(e, out var revDict))
                foreach (var kv in revDict)
                    removed.AddRange(kv.Value.Where(src => src != e).Select(src => (src, e, kv.Key)));
            foreach (var (src, tgt, type) in removed)
                Remove(src, tgt, type);
        }

        public List<(Entity src, Entity tgt, TKey type)> GetAllRelations(Entity e)
        {
            var result = new List<(Entity, Entity, TKey)>();
            if (_fwd.TryGetValue(e, out var fwdDict))
                foreach (var kv in fwdDict)
                    result.AddRange(kv.Value.Select(tgt => (e, tgt, kv.Key)));
            if (_rev.TryGetValue(e, out var revDict))
                foreach (var kv in revDict)
                    result.AddRange(kv.Value.Where(src => src != e).Select(src => (src, e, kv.Key)));
            return result;
        }

        public List<Entity> GetTargets(Entity src, TKey type) =>
            _fwd.TryGetValue(src, out var d) && d.TryGetValue(type, out var s) ? s.ToList() : new();

        public List<Entity> GetSources(Entity tgt, TKey type) =>
            _rev.TryGetValue(tgt, out var d) && d.TryGetValue(type, out var s) ? s.ToList() : new();

        public bool Has(Entity src, Entity tgt, TKey type) =>
            _fwd.TryGetValue(src, out var d) && d.TryGetValue(type, out var s) && s.Contains(tgt);

        private static HashSet<Entity> GetOrCreate(Dictionary<Entity, Dictionary<TKey, HashSet<Entity>>> dict, Entity key, TKey type)
        {
            if (!dict.TryGetValue(key, out var inner)) dict[key] = inner = new();
            if (!inner.TryGetValue(type, out var set)) inner[type] = set = new();
            return set;
        }
    }
}
