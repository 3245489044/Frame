@{
    # ===== IEF+ 执行契约门禁基线（纯数据单源；守卫 = Operation\DSL\Agent\Check-IntentContract.ps1）=====
    # 把设计单 §3.2 三张表落到「注册表声明结构事实」的机器判据（C1–C6）。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'IntentContract'
    Source = 'Frame\PowerShellScript\Operation\DSL\Agent\Check-IntentContract.ps1'

    # C1/C6 正对照下限：受检 Row 行数须 ≥ 此值（防授权面被掏空 ⇒ 判据无标的假绿）。
    MinRows = @{ Value = 20; Origin = 'Derived'; DueWhen = '' }

    # C4 正对照下限：PreconditionKind 枚举值数须 ≥ 此值（新增即上调）。
    MinPreconditionKind = @{ Value = 2; Origin = 'Derived'; DueWhen = '' }

    # C5：Parity CaseId 暂未在测试面命中者的登记表（避免假红；随测试补齐逐项销账）。
    PendingCaseIds = @{
        Value = @()
        Origin = 'Derived'
        DueWhen = 'CaseId 在测试面补齐后由本表销账'
    }
}
