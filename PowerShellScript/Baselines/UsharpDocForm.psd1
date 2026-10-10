@{
    # ===== UsharpDocForm 门禁基线（单源；守卫 = Operation\DSL\Check-UsharpDocForm.ps1）=====
    # 语义（V1/V2/V3-form + V9）：记录实例形态与纯三列由声明面结构事实锁定。
    Schema = 'gate-baseline-v1'
    Gate   = 'UsharpDocForm'

    # U1 兼容面残留上限（2026-10-10 实测：KvDocumentTable / KvJsonTable / [IndexJson] / ExtractScalar 全退役 ⇒ 0）
    ForbiddenLegacyTokens = @{ Value = 0; Origin = 'Derived'; DueWhen = '' }
    # U2 纯三列列数（[Id]/[NodeKey]/[Doc]）
    PureColumnCount = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
    # U3 tables.ushar 声明表数下限（tasks/nodes/histories；全表由 [Table] 声明派生）
    DeclaredTableCount = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
    # U7 存储层 JSON 出口白名单是否强制（true = 白名单外 JsonSerializer/JsonDocument 命中即红；白名单逐条登记于守卫脚本）
    JsonExitEnforce = @{ Value = $true; Origin = 'Derived'; DueWhen = '' }
    # U4 记录头渲染字面 "[Record] " 定义数（单源 = UsharpRecordDecl.ToCanonical）
    RecordHeaderRenders = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    # U5 存储层引 Usharp.* 的文件数（依赖方向：仅中立 Plate.DslMethod）
    StorageUsharpRefs = @{ Value = 0; Origin = 'Derived'; DueWhen = '' }
}
