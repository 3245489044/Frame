@{
    # 双载体互斥门基线（纯数据单源；守卫 = Operation\DSL\Check-DualCarrierExclusion.ps1）
    # 语义：端口块内 `impl delegate`（求值走 C# 提供者）与 `script [...]`（永不被求值的声明体）并存 = 僵尸脚本，
    #   新增即红（治 D3：同键双载体并存且无对账）。现存 5 处为存量债务，只减不增。
    # 首测 = 2026-10-07（F7 立门；实测命中 5 个端口，均落 NodeProcess.specs）。
    Schema = 'gate-baseline-v1'
    Gate   = 'DualCarrierExclusion'
    Source = 'Frame\PowerShellScript\Operation\DSL\Check-DualCarrierExclusion.ps1'

    # 存量僵尸端口集（只减不增；销账后须下调本列表）
    ZombiePorts = @{
        Value = @(
            'NodeGraphOrchestrator.RunRoundAsync',
            'NodeOutputPersistStage.PersistAndHydrate',
            'NodeProcessStepExecutorAdapter.ExecuteAsync',
            'NodeStepExecutionService.ExtractProps',
            'NodeTaskLinker.LinkTaskNodesAsync'
        )
        Origin  = 'Debt'
        DueWhen = 'S4 删注册层 / Fixtures 退场时清除僵尸 script（届时本列表归零）'
    }
}
