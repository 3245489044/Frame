@{
    # ===== UsharpGetParity 门禁基线（单源；守卫 = Operation\DSL\Check-UsharpGetParity.ps1）=====
    # 语义（V4/V5/V6-form）：usharp_get 注册单点 + 下推表达式同源 + 单语句 CAS。
    Schema = 'gate-baseline-v1'
    Gate   = 'UsharpGetParity'

    # G1 'UsharpSqlFunction.EnsureRegistered()' 调用点（唯一注册点 = UsharpDataPorts.EnsureSqlFunctions）
    EnsureRegisteredCallSites = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    # G2 存储层内联 JSON 路径字面量（'$.x'）计数（须经 UsharpRecordPaths）
    InlinePathLiterals = @{ Value = 0; Origin = 'Derived'; DueWhen = '' }
    # G3 usharp_get 表达式索引落点（TaskPool 2 + Snapshot 1）
    UsharpIndexSites = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
    # G4 单语句 CAS（SET [Doc]=@d ... usharp_get(Status)=@from）
    CasSingleStatementSites = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
}
