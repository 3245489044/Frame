@{
    # ===== DeclarativeSurface 门禁基线（单源；守卫 = Operation\DSL\Usharp\Check-DeclarativeSurface.ps1）=====
    # 语义：呈现策略（模板键/折叠词/面序/兜底模板/必需键/诊断码/标签）只允许在声明面；
    #       生产代码内策略字面量 = 0（新增只能改声明，或走显式登记并注明理由）。
    # 权威：Docs\analysis\2026\10\10.31\2026-11-01-框架声明式收敛-散布逻辑清单与实施方案.md
    Schema = 'gate-baseline-v1'
    Gate   = 'DeclarativeSurface'

    # 豁免计数（策略字面量命中上限；零豁免 ⇒ 改策略只能改声明）
    AllowCount = @{ Value = 0; Origin = 'Derived'; DueWhen = '常驻：声明式收敛后策略字面量恒 0；如需登记须同步方案文档并注明残留理由' }
}
