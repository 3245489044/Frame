
using System.Collections.Concurrent;
using BasicMethod.Block;

namespace Share.StateManagement
{
    public class StateOrchestrator : IStateOrchestrator
    {
        private readonly ConcurrentDictionary<string, IStateActor> _actors = new(StringComparer.OrdinalIgnoreCase);

        public StateOrchestrator(IStateStore store)
        {
            Store = PortGuard.Required(store, nameof(store));
        }

        public IStateStore Store { get; }

        public void RegisterActor(IStateActor actor)
        {
            PortGuard.Required(actor, nameof(actor));

            if (!_actors.TryAdd(actor.Name, actor))
            {
                throw new InvalidOperationException(
                    $"Actor '{actor.Name}' 已注册。每个 Actor 必须有唯一的 Name。");
            }
        }

        public IStateActor? GetActor(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            return _actors.TryGetValue(name, out var actor) ? actor : null;
        }

        public async Task<PipelineResult> ExecutePipelineAsync(ExecutionPipeline pipeline)
        {
            PortGuard.Required(pipeline, nameof(pipeline));

            var context = new BlockContext();
            context.Data["StateStore"] = Store;
            context.Data["StateOrchestrator"] = this;

            return await pipeline.RunAsync(context);
        }

        public async Task InitializeAllActorsAsync()
        {
            var exceptions = new List<Exception>();

            foreach (var actor in _actors.Values)
            {
                try
                {
                    await actor.InitializeAsync();
                }
                catch (Exception ex)
                {
                    exceptions.Add(new InvalidOperationException(
                        $"Actor '{actor.Name}' 初始化失败: {ex.Message}", ex));
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(
                    $"{exceptions.Count} 个 Actor 初始化失败:", exceptions);
            }
        }

        public async Task ShutdownAllActorsAsync()
        {
            var exceptions = new List<Exception>();

            foreach (var actor in _actors.Values)
            {
                try
                {
                    await actor.ShutdownAsync();
                }
                catch (Exception ex)
                {
                    exceptions.Add(new InvalidOperationException(
                        $"Actor '{actor.Name}' 关闭失败: {ex.Message}", ex));
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(
                    $"{exceptions.Count} 个 Actor 关闭失败:", exceptions);
            }
        }

        public void Reset()
        {
            Store.Clear();
            _actors.Clear();
        }

        public int ActorCount => _actors.Count;

        public IEnumerable<string> ActorNames => _actors.Keys;
    }
}
