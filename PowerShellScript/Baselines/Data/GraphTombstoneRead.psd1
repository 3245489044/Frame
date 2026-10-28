@{
    # ===== 图边墓碑读取点基线（纯数据单源；守卫 = Operation\DSL\Data\Check-GraphTombstoneRead.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'GraphTombstoneRead'
    Source = 'Frame\PowerShellScript\Operation\DSL\Data\Check-GraphTombstoneRead.ps1'

    # 写前裁决调用点下界（1 = 墓碑策略接入写路径）。
    AdmitCallMinimum = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '保持 ≥1；写入口收敛到 [Flow] GraphMount 后仍须 ≥1'
    }

    # 边墓碑读取点下界（1 = 删除意图有约束力）。
    EdgeTombstoneReadMinimum = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '保持 ≥1（只写不读 ⇒ 已删边复活）'
    }
}
