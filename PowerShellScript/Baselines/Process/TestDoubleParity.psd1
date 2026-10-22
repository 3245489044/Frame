@{
    # ===== TestDoubleParity 门禁基线（测试替身一致性：已报告的存量项）=====
    # 语义：本表为「已知/已报告」集合（棘轮）——在表即不算新违规（仍打印可见化）；表外即红（新增漂移）。
    # Origin ∈ {Derived,Debt,Verdict}（四分事实，见 Baselines\README.md）；Debt/Verdict 必带 DueWhen（销账条件）。
    Schema = 'gate-baseline-v1'

    Known  = @(
        @{
            Value   = 'NodeRecord.PayloadJson'
            Origin  = 'Debt'
            DueWhen = '测试替身随产品模型演进：NodeRecord（Frame\CsharpPrj\Operation\DataStoreDriven\Abstractions\NodeStore.cs）无 PayloadJson 成员，改经 NodeStore/载荷出口断言'
            Note    = '既有红（与本次框架级修复无关）：Agent.ClineUsage.Tests\ClineHistoryArchiveTests.cs 等 11 处按旧面构造 NodeRecord'
        }
        @{
            Value   = 'EventConfig.Click'
            Origin  = 'Debt'
            DueWhen = '厘清 EventConfig（Frame\CsharpPrj\Plate\TConfig\Models\EventConfig.cs，仅 EventsDictionary/Bindings）与测试侧 Click 的归属后改测试或补产品成员'
            Note    = '待核实：测试侧 new EventConfig { Click = … } 引用了产品类型未声明的成员（可能为同名/转换器语义误报，须人工确认）'
        }
    )
}