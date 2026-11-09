@{
    # ===== AssemblyClosure 门禁基线（单源；守卫 = Operation\DSL\Meta\Check-AssemblyClosure.ps1）=====
    # 语义：装载组「声明 ⇔ 清单 ⇔ 磁盘」三段闭包；判据载体 = 声明 ∪ deps.json ∪ 文件系统。
    Schema = 'gate-baseline-v1'
    Gate   = 'AssemblyClosure'

    # 正对照：装配组数下限（防声明面坍缩 ⇒ 假绿）
    MinGroups = @{ Value = 6; Origin = 'Derived'; DueWhen = '' }

    # 正对照：已存在根数下限（产物面坍缩保底；实测 filedriven/core/agent ≥3）
    MinRoots = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }

    # A3：函数库程序集归属必须唯一（0 = 缺，>1 = 多源）
    RequireSingleOwnership = @{ Value = $true; Origin = 'Verdict'; DueWhen = '函数库程序集归属改由构建声明派生后重裁' }
}
