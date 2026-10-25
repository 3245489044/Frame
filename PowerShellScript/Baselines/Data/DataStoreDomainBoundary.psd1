@{
    # ===== DataStoreDomainBoundary 门禁基线（单源；守卫 = Operation\DSL\Data\Check-DataStoreDomainBoundary.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'DataStoreDomainBoundary'

    # Operation 同层引用（只减不增）：18 条历史存量 + 1 条控制反转新增（Domains → 核心，单向）
    SameLayerRefBaseline = @{ Value = 21; Origin = 'Debt'; DueWhen = '只减不增；新增须先在方案 §8 裁决并登记理由' }
    # 核心域 import 文件数（正对照下限；迁移完成后 → 0，销账后删除本行）
    CoreDomainUsingFilesBaseline = @{ Value = 11; Origin = 'Debt'; DueWhen = 'D-2/D-3/D-4 迁移完成后 → 0' }
    # Core\Domain 文件数（正对照下限；D-4 后 → 0，销账后删除本行）
    CoreDomainFilesBaseline = @{ Value = 10; Origin = 'Debt'; DueWhen = 'D-4 后 → 0' }
}
