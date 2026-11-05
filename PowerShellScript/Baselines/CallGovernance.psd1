@{
    # ===== 调用治理注册表（CallGovernance）=====
    # 语义：把「是否允许这次调用 / 调用是否有效 / 是否有增益 / 失败归谁」从执行者判断搬成宿主判定。
    #   权威公理 = .clinerules\axioms.md AX.CallAdmissibility（缺一即拒绝）
    #   裁决原语单源 = Baselines\CallGovernor.psm1（禁各消费方手写解析）
    #   消费方 = Frame\Invoke\Invoke-Frame.ps1（唯一 shell 入口）· Operation\DSL\Tooling\Locate-Failure.ps1（归属查表）
    #   守卫 = Operation\DSL\Tooling\Check-CallGovernance.ps1
    # 本文件非单件门禁基线（`_` 前缀族外），不进入 GateBaselines 单件判据。
    Schema = 'call-governance-v1'

    # shadow = 只记录+可见化（不拒绝）；enforce = 按 ReasonCodes 拒绝（fail-closed）
    # 迁移期默认 shadow：先拿到「有多少次无凭证/无区分力调用」的实测，再翻 enforce。
    Mode = 'shadow'

    # 低风险动作（免票据/免区分力）：产品运行时归一面，不承担「证据采集」职责。
    # 判据载体 = 动作名表项（结构事实）；新增项须同时说明为何不产生 verifiable 结论。
    LowRiskActions = @('launch', 'invoke-launch-selftest', 'launch-smoke')

    # 无票据是否拒绝（AX.CallAdmissibility I1）；shadow 下仅记 R1 可见化
    RequireTicket = $true

    # 区分力必填（新增第五要素）；缺 ⇒ R1
    RequireDiscrimination = $true

    # 拒绝原因码（受控词 → 线格式值；判据见 axioms AX.CallAdmissibility）
    ReasonCodes = @{
        OK = 'ok'
        R1 = 'no-ticket'          # 票据/区分力缺失
        R2 = 'arg-contract'       # 未声明参数被静默丢弃（-Switch value 错位类）
        R3 = 'echo-mismatch'      # 生效 ≠ 声明
        R4 = 'budget-exhausted'   # 分账/总账达上限
        R5 = 'zero-gain-repeat'   # 同区分力二次执行 / 零增益累计
        R6 = 'phase-violation'    # 相位白名单外
        R7 = 'attribution-unsettled' # 归属未判即继续探索
        R8 = 'mergeable'          # 同批同目的可合并
    }

    # 分账上限（数值权威 = .clinerules\axioms.md §9；本表仅操作口径）
    Budgets = @{
        shell    = 8
        diagnose = 4
        verify   = 2
    }

    # 动作 → 账别（未登记动作计入 shell 总账）
    ActionClass = @{
        'invoke-verify'        = 'verify'
        'invoke-regression'    = 'verify'
        'invoke-buildeffective'= 'verify'
        'invoke-gatecheck'     = 'verify'
        'search-repo'          = 'diagnose'
        'locate-test'          = 'diagnose'
        'locate-build'         = 'diagnose'
        'locate-port'          = 'diagnose'
        'locate-failure'       = 'diagnose'
        'read-agentdb'         = 'diagnose'
        'test-jsonfiles'       = 'diagnose'
        'state-root-audit'     = 'diagnose'
        'task-consistency-audit' = 'diagnose'
    }

    # 相位禁止表（default-deny 的显式清单；未登记相位 = 不施加相位约束）
    PhaseDeny = @{
        diagnose = @('invoke-verify', 'invoke-regression', 'invoke-buildeffective', 'invoke-gatecheck')
        edit     = @('invoke-verify', 'invoke-regression', 'invoke-buildeffective', 'invoke-gatecheck', 'search-repo', 'locate-test', 'locate-build', 'locate-port', 'read-agentdb', 'test-jsonfiles')
        verify   = @()
    }

    # 归属查表（R7）：已知红名单 + 改动面来源
    Attribution = @{
        KnownFailures = 'Environment\CentralTest\Manifests\known-failures.json'
        ChangedSource = 'git'      # git = git status --porcelain（含子模块）
    }

    # 账本落点（唯一可写数据根）
    LedgerDir = 'Data\Tests\call-ledger'
}
