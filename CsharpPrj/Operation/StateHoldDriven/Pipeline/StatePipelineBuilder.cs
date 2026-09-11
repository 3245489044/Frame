
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

            // 2026-08-05 StateCenter 收敛：默认管线末尾追加 StateCenterBlock（创建 StateCenter + 注册）
            // 2026-09-12 DataCenter 立项：StateCenterBlock 只吃通用 StateStore；产品端口经 OperationStateStore 承载。
            pipeline.AddBlock(new StateCenterBlock(store as Operation.StateBlock.Core.StateStore
                ?? throw new InvalidOperationException(
                    "StateCenterBlock 需要 IStorageCell 后端的 StateStore（经 OperationStateStore 提供统一存储）。")));


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
