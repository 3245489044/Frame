@{
    # ===== RelationPlan 门禁基线（单源；守卫 = Operation\DSL\Node\Check-RelationPlan.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'RelationPlan'

    # C# 归属边字面量文件数上限（只降不升）：E7 管声明侧字面量，C4（WriteSurface）管写入口。
    # 复算：pwsh Frame\PowerShellScript\Operation\DSL\Node\Check-RelationPlan.ps1 -ShowDetail → E7 读数。
    E7MaxLiteralFiles = @{ Value = 4; Origin = 'Derived'; DueWhen = '字节码清理（字面量归一到 ReferenceEdgeTypes）后同批下调' }
}
