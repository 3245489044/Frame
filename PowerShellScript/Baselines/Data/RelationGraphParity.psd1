@{
    # ===== 关系图三方对账基线（纯数据单源；守卫 = Operation\DSL\Data\Check-RelationGraphParity.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'RelationGraphParity'
    Source = 'Frame\PowerShellScript\Operation\DSL\Data\Check-RelationGraphParity.ps1'

    # [Mount] 受控字段（声明面 = Config\relations\mounts.ushar；权威词表 = MountStages/MountFamilies/MountPostures）。
    ControlledStage = @{
        Value   = @('live', 'provision', 'settle', 'explicit', 'rebuild')
        Origin  = 'Derived'
        DueWhen = '新增阶段时先改 MountStages 常量，再同步本表'
    }

    ControlledFamily = @{
        Value   = @('structural', 'presentation')
        Origin  = 'Derived'
        DueWhen = '新增族时先改 MountFamilies 常量，再同步本表'
    }

    ControlledPosture = @{
        Value   = @('require', 'best-effort', 'reduced')
        Origin  = 'Derived'
        DueWhen = '新增姿态时先改 MountPostures 常量，再同步本表'
    }

    # 违例容差（0 = 任一违例即红）。
    ViolationTolerance = @{
        Value   = 0
        Origin  = 'Derived'
        DueWhen = '保持 0（禁默认值兜底）'
    }
}
