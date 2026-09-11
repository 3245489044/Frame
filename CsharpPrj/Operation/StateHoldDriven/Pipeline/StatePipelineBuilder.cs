
using DataCenter.Adapters;
using Operation.StateBlock.Abstractions;
using Operation.StateBlock.Blocks;
using StatePipeline = Share.StateManagement.StatePipeline;

namespace Operation.StateBlock.Pipeline
{
    public static class StatePipelineBuilder
    {
        public static StatePipeline CreateDefaultPipeline(IStateStore store)
        {
            var pipeline = new StatePipeline("StateInitialization", "默认状态初始化管线");

            pipeline.AddBlock(new RegistryStateBlock(store));

            pipeline.AddBlock(new FunctionStorageStateBlock(store));

            pipeline.AddBlock(new FileDrivenStateBlock(store));

            pipeline.AddBlock(new ExecutorStateBlock(store));

            pipeline.AddBlock(new AppServiceStateBlock(store));

            // 2026-08-05 StateCenter 收敛：默认管线末尾追加状态中心初始化块（创建 StateCenter + 注册）
            // 2026-09-12 DataCenter G2：初始化块迁出 Core → DataCenter.Adapters 的 BlockStateInitializer（宿主设施适配）。
            pipeline.AddBlock(new BlockStateInitializer(store as Operation.StateBlock.Core.StateStore
                ?? throw new InvalidOperationException(
                    "BlockStateInitializer 需要 IStorageCell 后端的 StateStore（经 OperationStateStore 提供统一存储）。")));


            return pipeline;
        }

        public static StatePipeline CreateMinimalPipeline(IStateStore store)
        {
            var pipeline = new StatePipeline("MinimalStateInit", "最小状态初始化管线");

            pipeline.AddBlock(new RegistryStateBlock(store));

            return pipeline;
        }

        public static StatePipeline CreateCustomPipeline(
            IStateStore store,
            Action<StatePipeline> configure)
        {
            var pipeline = new StatePipeline("CustomStateInit", "自定义状态初始化管线");
            configure(pipeline);
            return pipeline;
        }
    }
}
