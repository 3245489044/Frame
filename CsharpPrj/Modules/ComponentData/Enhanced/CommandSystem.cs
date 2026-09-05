using ECS.Core;
using System.Collections.Concurrent;

namespace Modules.ComponentData.Enhanced
{
    public class CommandSystem<TCommand, TResult> : SystemBase where TCommand : CommandBase<TResult>
    {
        private readonly ConcurrentQueue<TCommand> _queue = new();
        public void Submit(TCommand cmd) => _queue.Enqueue(cmd);
        public sealed override void Update(float deltaTime)
        {
            while (_queue.TryDequeue(out var cmd))
            {
                try { cmd.Execute(); }
                catch (Exception ex) { if (!cmd.Task.IsCompleted) cmd.SetException(ex); }
            }
        }
    }
}
