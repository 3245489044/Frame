@{
    # ===== TestLayout 结构基线（FA1 路径镜像 / TD1 债务到期）=====
    # 语义：不是「测试→源码」归属表；只登记**结构面存量缺口**（集合棘轮：只减不增）。
    #   消费方：Environment\CentralTest\Tools\Gate\TestLayout\Check-TestPathMirror.ps1 ∧ Check-TestLayoutDebt.ps1
    #   纪律：Allowed 为一次性冻结存量；集合外新增未映射工程 ⇒ 立即红（改名/换位置同样被捕获）。
    #   口径：Lane ∈ {Strict, SpecOnly} 的测试工程目录；已由 L0 映射 / 命名变体 / Adapters.NonTestPattern 覆盖的不计入；
    #        车道根目录与辅助工程不判（待 U12 逐项归类）。
    Schema  = 'test-layout-structure-v1'
    Derived = '2026-11-06'

    PathMirrorUnmapped = @{
        Value   = 33
        Origin  = 'Debt'
        DueWhen = '2027-03-31 逐项改名/补 L0 容器映射后归零'
        Allowed = @(
            'Environment\CentralTest\AgentTest\Agent.Api.Contract.Tests'
            'Environment\CentralTest\AgentTest\Agent.Api.Tests'
            'Environment\CentralTest\AgentTest\Agent.ClineUsage.Tests'
            'Environment\CentralTest\AgentTest\Agent.Observe.Tests'
            'Environment\CentralTest\AgentTest\Agent.Operation.Tests'
            'Environment\CentralTest\AgentTest\Agent.Persistence.Tests'
            'Environment\CentralTest\AgentTest\Agent.Runtime.Tests'
            'Environment\CentralTest\AgentTest\Boundary.Tests'
            'Environment\CentralTest\AgentTest\Plate.HttpMethod.Tests'
            'Environment\CentralTest\AgentTest\ReferenceSchema.Tests'
            'Environment\CentralTest\AgentTest\StorageTemplate.Tests'
            'Environment\CentralTest\CSharpTest\BasicMethod.Tests\MetaCommand.Tests'
            'Environment\CentralTest\CSharpTest\Integration.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Standards.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\Declarative.Ledger.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\EngineViewer.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\EventTriggerFramework.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\FunctionStorage.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\MarkdownViewer.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\OperationSpec.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\OperationUnit.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\Plate.JsonMethod.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\Vm.Tests'
            'Environment\CentralTest\CSharpTest\Operation.Tests\WdUI.Tests'
            'Environment\CentralTest\CSharpTest\PythonEnvironment.Tests'
            'Environment\CentralTest\CSharpTest\Share.Tsets\BlockKernel.Tests'
            'Environment\CentralTest\CSharpTest\Share.Tsets\EngineFrameworks.Tests'
            'Environment\CentralTest\CSharpTest\Share.Tsets\RefreshFramework.Tests'
            'Environment\CentralTest\CSharpTest\Share.Tsets\Registry.Tests'
            'Environment\CentralTest\CSharpTest\Sync.SpecTests'
            'Environment\CentralTest\UsharpTests\Usharp.Adapters.Tests'
            'Environment\CentralTest\UsharpTests\Usharp.Tests'
        )
    }
}
