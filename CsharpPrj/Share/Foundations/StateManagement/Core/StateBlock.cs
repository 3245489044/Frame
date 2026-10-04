
using BasicMethod.Block;

namespace Share.StateManagement
{
    public abstract class StateBlock : BlockBase
    {
        protected StateBlock(
            string name,
            IStateStore store,
            string? description = null)
            : base(name, description ?? $"状态块: {name}")
        {
            Store = PortGuard.Required(store, nameof(store));
        }

        protected IStateStore Store { get; }

        public override abstract Task<BlockResult> ExecuteAsync(BlockContext context, CancellationToken cancellationToken = default);
    }
}
