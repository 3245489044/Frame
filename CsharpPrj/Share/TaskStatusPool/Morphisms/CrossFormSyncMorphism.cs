
#nullable enable
using Share.TaskStatusPool.Abstractions;

namespace Share.TaskStatusPool.Morphisms
{
    public class CrossFormSyncMorphism : IOrderedMorphism<IPoolContext, IPoolContext>
    {
        private readonly ControlPool.ControlPool _controlPool;

        public string Name => "CrossFormSync";

        public int Order => 4;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> _defaultSyncMap
            = new(System.StringComparer.OrdinalIgnoreCase);

        protected readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> SyncMap;

        public CrossFormSyncMorphism(
            ControlPool.ControlPool controlPool,
            System.Collections.Concurrent.ConcurrentDictionary<string, string[]>? syncMap = null)
        {
            _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
            SyncMap = syncMap ?? _defaultSyncMap;
        }

        public static void RegisterSync(string sourceControlName, params string[] targetControlNames)
        {
            ArgumentNullException.ThrowIfNull(sourceControlName);
            ArgumentNullException.ThrowIfNull(targetControlNames);

            _defaultSyncMap[sourceControlName] = targetControlNames;
        }

        public static void RegisterSyncs(Dictionary<string, string[]> mappings)
        {
            ArgumentNullException.ThrowIfNull(mappings);

            foreach (var kvp in mappings)
            {
                _defaultSyncMap[kvp.Key] = kvp.Value;
            }
        }

        public static void ClearSyncs()
        {
            _defaultSyncMap.Clear();
        }

        public IPoolContext Morph(IPoolContext input)
        {
            var changedControl = input.Get<string>("_changedControl");
            if (string.IsNullOrEmpty(changedControl))
                return input;

            if (!SyncMap.TryGetValue(changedControl, out var targets) || targets == null || targets.Length == 0)
                return input;

            var value = _controlPool.Read(changedControl);

            var writtenControls = new List<string>();
            foreach (var target in targets)
            {
                _controlPool.Write(target, value);
                writtenControls.Add(target);
            }

            input.Set("_syncTargets", writtenControls.ToArray());
            input.Set("_syncSource", changedControl);

            return input;
        }
    }
}
