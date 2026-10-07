@{
    # 双载体互斥门基线（纯数据单源；守卫 = Operation\DSL\Check-DualCarrierExclusion.ps1）
    # 语义：端口块内 `impl delegate`（求值走 C# 提供者）与 `script [...]`（永不被求值的声明体）并存 = 僵尸脚本，
    #   新增即红（治 D3：同键双载体并存且无对账）。2026-10-07 存量已归零（见下）。
    # 首测 = 2026-10-07（F7 立门；实测命中 5 个端口，均落 NodeProcess.specs）。
    Schema = 'gate-baseline-v1'
    Gate   = 'DualCarrierExclusion'
    Source = 'Frame\PowerShellScript\Operation\DSL\Check-DualCarrierExclusion.ps1'

    # 存量僵尸端口集（只减不增；销账后须下调本列表）
    # 2026-10-07：5 处死 script 体已清除（NodeProcess.specs 收敛为纯 delegate）⇒ 归零。
    ZombiePorts = @{
        Value   = @()
        Origin  = 'Debt'
        DueWhen = ''
    }
}
