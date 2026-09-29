@{
    # ===== NodeHierarchyRefs 门禁基线（单源；守卫 = Modules\Node\Check-NodeHierarchyRefs.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'NodeHierarchyRefs'

    # 声明面边类型数下限（W1 实测收敛 = 4；追加须同批更新本基线并给出理由）
    DeclaredEdgeCount = @{ Value = 4; Origin = 'Derived'; DueWhen = '可由 hierarchy-refs.ushar 边类型数复算 ⇒ 后续改由生成器产出' }
    # 边类型常量数下限（防常量文件被清空 ⇒ 对账恒真）
    ConstQuantity     = @{ Value = 4; Origin = 'Derived'; DueWhen = '可由常量文件解析数复算 ⇒ 后续改由生成器产出' }
}
