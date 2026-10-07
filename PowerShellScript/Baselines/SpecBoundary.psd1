@{
    # ===== SpecBoundary 门禁基线（单源；守卫 = Operation\DSL\Check-SpecBoundary.ps1）=====
    # 语义：spec 生成面 / 手写面的**边界结构事实**（R0 边界冻结）。
    #   手写真源（*.specs / projects.specs / 端口声明）落源码树；机器产物（.spec.json / 壳类 / 常量表）
    #   落生成根（BuildOutput\<Cfg>\SpecGen\，被 .gitignore 的 BuildOutput/ 覆盖）。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'SpecBoundary'

    # 手写白名单（源码树只允许这些"写"；glob 相对仓库根，仅作登记，判据看生成物侧）。
    HandwrittenPatterns = @{
        Value = @(
            'Environment\CentralTest\AgentTest\**\Cases\**\*.specs'
            'Environment\CentralTest\AgentTest\**\Standards\ports\*.specs'
            'Environment\CentralTest\projects.specs'
            'Environment\CentralTest\Tools\**\*.ps1'
            'Frame\Invoke\**\*.ps1'
        )
        Origin = 'Derived'
        DueWhen = ''
    }

    # 生成根（产物落点；不入库/不进源码树）。
    GeneratedRoots = @{
        Value = @(
            'Environment\CentralTest\BuildOutput'
        )
        Origin = 'Derived'
        DueWhen = ''
    }

    # 生成物文件名模式（源码树内出现即违规，白名单除外；判据函数由此驱动 = 单源）。
    GeneratedPatterns = @{
        Value = @(
            '*SpecTests.g.cs'
            'SpecPorts.g.cs'
            '*.spec.json'
        )
        Origin = 'Derived'
        DueWhen = ''
    }

    # 手写遗留 .spec.json（tracked、无 auto-generated 头）；R4 SEP 批迁移为 .specs 后销账。
    HandwrittenLegacyJson = @{
        Value = @(
            'Environment\CentralTest\TestInfrastructure.Tests\Cases\adapter-demo\adapter-demo.spec.json'
            'Environment\CentralTest\TestInfrastructure.Tests\Cases\spec-kernel\spec-kernel.spec.json'
        )
        Origin = 'Debt'
        DueWhen = 'R4 SEP 批把这两件迁移为 .specs 真源后删除本键值'
    }

    # 正对照下限：手写白名单模式数须 ≥ 此值（防清单被掏空 ⇒ 判据无标的假绿）。
    MinHandwrittenPatterns = @{
        Value = 4
        Origin = 'Derived'
        DueWhen = ''
    }
}
