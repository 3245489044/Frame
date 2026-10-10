@{
    # ===== DomainWireSingleSource 门禁基线（单源；守卫 = Modules\Node\Check-DomainWireSingleSource.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'DomainWireSingleSource'

    # D3 正对照下限：声明面（Config\nodes\*.ushar 的 [Domain]→placements）位置并集数 ≥ 此值（防声明面被掏空 ⇒ 判据无标的假绿）
    # 2026-10-10 实测：14 域 15 位置（原 9 域 14 位置 + U5 新增 resource 读入域 ⇒ resource-node）
    DomainCount = @{ Value = 15; Origin = 'Derived'; DueWhen = 'SR-3c：位置行由 loc 词集 + 域归属展开生成后，本值改由生成器产出' }

    # D2 散弹点上限（报告模式起步，只减不增）：per-domain 形态去重计数
    # 2026-10-06 会话域单节点化：新增 session-view / message-stream / segment-stream 三域各一槽键常量引用（8 → 11）。
    SlotKeyConstants     = @{ Value = 11; Origin = 'Debt'; DueWhen = 'R2 收口：逐域槽键常量改经 NodeDomainDescriptor.SlotKeyOf 派生后归零' }
    SubPathConstants     = @{ Value = 0;  Origin = 'Derived'; DueWhen = 'R1 已达成：描述行不再引用 AgentNodeSpec.<X>SubPath（常量留存为回退面，零用量）' }
    # 2026-10-06 会话域单节点化：新增 session-view / message-stream / segment-stream 三域各一 Create<X>（8 → 10）。
    CarrierFactoryMethods = @{ Value = 10; Origin = 'Debt'; DueWhen = 'R1c 收口：per-domain Create<X> 改通用工厂 SqlNodeCarrierFactory.FromDeclaration 后归零' }
}
