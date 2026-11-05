
#nullable enable
using System.Collections.Concurrent;
using TConfig.ContextFrame.Abstractions;

namespace Modules.InternalStorage.Integration
{
    public class EcsSessionManager : IDisposable
    {
        private readonly IStorageEngine _engine;
        private readonly ConcurrentDictionary<string, HashSet<string>> _sessionComponents = new();

        public EcsSessionManager(IStorageEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public IStorageEngine Engine => _engine;

        public IStorageCell RegisterEcsFrame(string sessionId, object ecsFrame)
        {
            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));
            if (sessionId.Length == 0)
                throw new ArgumentException("会话标识不能为空。", nameof(sessionId));
            if (ecsFrame == null)
                throw new ArgumentNullException(nameof(ecsFrame));

            var cell = _engine.CreateSession(sessionId);

            cell.Set("ecs_frame_type", ecsFrame.GetType().FullName ?? ecsFrame.GetType().Name, StorageLayer.Config);

            _sessionComponents.TryAdd(sessionId, new HashSet<string>());

            return cell;
        }

        public void UnregisterEcsFrame(string sessionId)
        {
            _engine.DisposeSession(sessionId);
            _sessionComponents.TryRemove(sessionId, out _);
        }

        public void SyncComponentData<T>(string sessionId, string componentKey, T componentData)
        {
            var cell = _engine.GetSession(sessionId);
            if (cell == null) return;

            cell.Set(componentKey, componentData, StorageLayer.Session);

            if (_sessionComponents.TryGetValue(sessionId, out var components))
            {
                components.Add(componentKey);
            }
        }

        public T? ReadComponentData<T>(string sessionId, string componentKey)
        {
            var cell = _engine.GetSession(sessionId);
            return cell == null ? default : cell.Get<T>(componentKey, StorageLayer.Session);
        }

        public IReadOnlySet<string> GetSyncedComponents(string sessionId)
        {
            return _sessionComponents.TryGetValue(sessionId, out var components)
                ? components
                : new HashSet<string>();
        }

        public ILayeredSnapshot SaveSnapshot(string sessionId)
        {
            return _engine.CreateSnapshot(sessionId);
        }

        public void RestoreSnapshot(string sessionId, ILayeredSnapshot snapshot)
        {
            _engine.RestoreSnapshot(sessionId, snapshot);
        }

        public IEnumerable<string> GetActiveSessionIds()
        {
            return _engine.GetActiveSessionIds();
        }

        public void Dispose()
        {
            foreach (var sessionId in _engine.GetActiveSessionIds().ToList())
            {
                _engine.DisposeSession(sessionId);
            }
            _sessionComponents.Clear();
        }
    }
}
