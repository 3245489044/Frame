@{
    # ===== DomainWireSingleSource 门禁基线（单源；守卫 = Modules\Node\Check-DomainWireSingleSource.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'DomainWireSingleSource'

    # D3 正对照下限：声明面（Config\nodes\*.json）位置并集数 ≥ 此值（防声明面被掏空 ⇒ 判据无标的假绿）
    # 2026-10-06 实测：6 域 10 位置（conversation 4 / segment 2 / message 1 / process 1 / task-node 1 / observability 1）
    DomainCount = @{ Value = 10; Origin = 'Derived'; DueWhen = 'SR-3c：位置行由 loc 词集 + 域归属展开生成后，本值改由生成器产出' }

    # D2 散弹点上限（报告模式起步，只减不增）：per-domain 形态去重计数
    SlotKeyConstants     = @{ Value = 8;  Origin = 'Debt'; DueWhen = 'R2 收口：逐域槽键常量改经 NodeDomainDescriptor.SlotKeyOf 派生后归零' }
    SubPathConstants     = @{ Value = 0;  Origin = 'Derived'; DueWhen = 'R1 已达成：描述行不再引用 AgentNodeSpec.<X>SubPath（常量留存为回退面，零用量）' }
    CarrierFactoryMethods = @{ Value = 8; Origin = 'Debt'; DueWhen = 'R1c 收口：per-domain Create<X> 改通用工厂 SqlNodeCarrierFactory.FromDeclaration 后归零' }
}
