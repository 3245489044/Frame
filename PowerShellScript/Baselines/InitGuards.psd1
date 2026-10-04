@{
    # ===== 初始化护栏门禁基线（纯数据单源；守卫 = Operation\DSL\Check-InitGuards.ps1）=====
    # 语义：把散落的空值护栏（`throw new ArgumentNullException` / `ArgumentNullException.ThrowIfNull`）
    #   收敛为统一门面 `PortGuard.Required`（T 出口）。两条旧形只减不增，门面用量只增不减。
    # 首测 = 2026-10-04（P1 试点批：Share.TaskStatusPool.Extensions.MorphismExtensions 10 处收编）。
    # 2026-10-04 P1-a 试点（Strategy.DecisionRules 19 处收编）：G1 1940→1921、G3 11→30。
    # 2026-10-04 P1-a Obs-1（Strategy.Observability 24 处收编）：G1 1921→1910、G2 993→980、G3 30→54。
    # 2026-10-04 P1-a Obs-2（Strategy.Observability 30 处收编）：G1 1910→1903、G2 980→957、G3 54→84。
    # 2026-10-04 P1-a Obs-3a（Strategy.Observability.Ports 63 处收编）：G1 1903→1892、G2 957→905、G3 84→147。
    # 2026-10-04 P1-a Obs-3b（Strategy.Observability.SpecHost 21 处收编）：G1 1892→1883、G2 905→893、G3 147→168。
    # 2026-10-04 P1-a Share-1a（Share.Frameworks.GraphFramework 90 处收编；8 处 TKey 值类型守卫保留）：G1 1883→1793、G3 168→258。
    # 2026-10-04 P1-a Share-1b（RoslynJson 8 / Diagnostics 4 / AiScheduling 2）：G1 1793→1779、G3 258→272。
    # 2026-10-04 P1-a Share-2（Orchestration：TaskStatusPool/EventBindingOrchestrator/HostAssembly/Adapters 111 处收编；15 处泛型值类型守卫保留）：G1 1779→1737、G2 893→824、G3 272→383。
    # 2026-10-04 P1-a Share-3（Foundations：StateManagement/FrameworkAbstractions/TreeTraceFramework/ConfigurationModels/ScopeFramework 81 处收编；19 处泛型值类型/?.形式保留）：G1 1737→1692、G2 824→788、G3 383→464。
    Schema  = 'gate-baseline-v1'
    Gate    = 'InitGuards'
    Source  = 'Frame\PowerShellScript\Operation\DSL\Check-InitGuards.ps1'

    # `throw new ArgumentNullException(...)` 计数上限（只减不增）。
    ArgumentNullExceptionThrow = @{
        Value   = 1692
        Origin  = 'Debt'
        DueWhen = 'P1 按子系统分批替换为 PortGuard.Required 至 0'
    }

    # `ArgumentNullException.ThrowIfNull(x)` 计数上限（只减不增）。
    ThrowIfNull = @{
        Value   = 788
        Origin  = 'Debt'
        DueWhen = 'P1 按子系统分批替换为 PortGuard.Required 至 0'
    }

    # `PortGuard.Required(...)` 计数下限（只增不减；防门面被删/回退）。
    PortGuardRequired = @{
        Value   = 464
        Origin  = 'Debt'
        DueWhen = 'P1 各批完成后按实测收紧下限'
    }
}
