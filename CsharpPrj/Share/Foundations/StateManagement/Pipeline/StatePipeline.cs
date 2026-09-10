
using BasicMethod.Block;

namespace Share.StateManagement
{
    public class StatePipeline : ExecutionPipeline
    {
        public StatePipeline(string name, string? description = null)
            : base(name, description ?? $"状态管线: {name}")
        {
        }

        public void AddBlock(BlockBase block)
        {
            ArgumentNullException.ThrowIfNull(block);
            _blocks.Add(block);
        }

        public async Task<PipelineResult> RunAsync(BlockContext context)
        {
            return await base.RunAsync(context);
        }
    }
}
