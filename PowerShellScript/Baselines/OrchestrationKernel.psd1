@{
    # 编排内核单一性守卫（OrchestrationKernel）判据基线。
    # 只读结构事实；脚本：Operation\DSL\Process\Check-OrchestrationKernel.ps1
    Schema = 'orchestration-kernel-v1'

    # C1：Block 内核必须零 ProjectReference（叶子层）
    KernelMustBeLeaf = $true

    # C2：BasicMethod 禁引的命名空间 / 项目变量
    ForbiddenNamespaces = @('Modules.Pipeline', 'Share.CircuitFramework')
    ForbiddenProjectRefs = @('Proj_Pipeline', 'Proj_CircuitFramework')

    # C3：唯一执行器与重试实现标记
    RunnerFile = 'BlockRunner.cs'
    RunnerRetryMarker = 'ExecuteWithPolicyAsync'
    LegacyRetryMarkers = @('ExecuteWithRetryAsync', 'RunWithPolicyAsync')

    # C4：唯一结果类型
    ResultType = 'BlockRunResult'
    LegacyResultType = 'PipelineResult'
    MaxLegacyResultDefinitions = 1

    # C5：Pipeline 适配层必须委托内核
    PipelineKernelMarkers = @('BlockRunner.Shared', 'StageBlockAdapter', 'BlockContextBridge')

    # C6：Circuit 执行体必须走 BlockPipeline
    CircuitKernelMarker = 'BlockPipeline'
}
