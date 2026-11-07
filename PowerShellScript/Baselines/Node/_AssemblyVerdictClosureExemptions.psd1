@{
    # 装配裁决闭门（AssemblyVerdictClosure）的**唯一**豁免登记册。
    # 原则：先保证判据正确，再逐条登记存量红；禁止通过放宽判据消红。
    #
    # ReadinessCoverage.Waived = AV2 的「显式不适用」（NotApplicableDeclared）：
    #   未被就绪核心覆盖的节点域必须登记在这里，并写明 Reason / DueWhen / Owner；
    #   未登记 = 隐式跳过 = 红（框架 Test-GateCriterionInput 语义）。
    #
    # Exemptions = 其余 AV1/AV3..AV9 的存量红；每条必须含 Rule / Subject / Reason / DueWhen / Owner。
    #   Reason   = 为什么现在不修（归属哪条会话/受哪条约束）
    #   DueWhen  = 什么条件下必须消掉（可验收的触发条件）
    #   Owner    = 责任面
    # 规则码见 Environment\CentralTest\Tools\Gate\Node\Check-AssemblyVerdictClosure.ps1 的 AV1..AV9。

    Schema = 'assembly-verdict-closure-exemptions-v1'
    Gate   = 'AssemblyVerdictClosure'

    # AV2 已声明的覆盖：必须与 AgentReadiness 真实核心集合一致；不一致即红。
    ReadinessCoverage = @{
        Covered = @('conversation')
        Waived  = @(
            @{ Domain = 'segment'
               Reason = '就绪矩阵核心 = db/taskpool/conversation/llm；segment 域由节点装配报告 AllAttached 与聚拢健康面判定，未纳入核心矩阵。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
            @{ Domain = 'message'
               Reason = '同上：核心四键不含 message；当前由装配报告判定，不由就绪矩阵判定。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
            @{ Domain = 'process'
               Reason = '同上：核心四键不含 process；当前由装配报告判定，不由就绪矩阵判定。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
            @{ Domain = 'observability'
               Reason = '同上：核心四键不含 observability；观测域为旁路面，不就绪不阻断装配。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
            @{ Domain = 'task-node'
               Reason = '同上：核心四键不含 task-node；任务节点域由 taskpool 键间接覆盖，未直接进入核心集合。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
            @{ Domain = 'archive'
               Reason = '同上：核心四键不含 archive；归档域为后置面，不阻断首屏装配。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
            @{ Domain = 'session-view'
               Reason = '同上：核心四键不含 session-view；会话视图域由 conversation 键间接覆盖。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
            @{ Domain = 'message-stream'
               Reason = '同上：核心四键不含 message-stream；流式派生域，由 message 域承载。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
            @{ Domain = 'segment-stream'
               Reason = '同上：核心四键不含 segment-stream；流式派生域，由 segment 域承载。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
            @{ Domain = 'raw-node'
               Reason = '同上：核心四键不含 raw-node；原始节点域为存储底层，不进入首屏就绪判定。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
            @{ Domain = 'resource'
               Reason = '同上：核心四键不含 resource；资源域为旁路面，不就绪不阻断装配。'
               DueWhen = '节点域纳入就绪矩阵核心判定集合，或就绪矩阵显式声明该域为可选且消费方按未就绪处理。'
               Owner = 'Agent 装配/就绪矩阵' }
        )
    }

    Exemptions = @(
        # ── AV1 位置对账：unresolved ⇒ 空串（运行期裁决修复归另一会话；本件只登记）──
        @{ Rule = 'AV1'; Subject = 'AgentInstanceHosting.ReconcilePlacements'
           Reason = 'spec=null 落进 unresolved 后返回空串，漂移/未解析不进入装配裁决。运行期裁决面不在本条会话改动范围（禁改 Agent 侧源码）。'
           DueWhen = 'ReconcilePlacements 对未解析域返回非空描述并被装配报告判红，或 AllAttached 以前置未解析数为 0 才可达。'
           Owner = 'Agent 装配裁决（运行期）' }

        # ── AV3 指标口径：首屏成功来自 UI 控件计数 ──
        @{ Rule = 'AV3'; Subject = 'MainForm.OnLoad'
           Reason = '首屏成功判据只读 renderStats（11 个 UI 控件），无节点域裁决；UI 源码不在本条会话改动范围。'
           DueWhen = 'MainForm 成功判据接入节点域裁决（nodeAssemblyVerdict / nodeDomainReady）且与 UI 计数分离。'
           Owner = '首屏/UI 裁决面' }

        # ── AV4 调用面 Shadow：结果不消费 ──
        @{ Rule = 'AV4'; Subject = 'InvocationContractGate.Evaluate'
           Reason = 'Blocking = mode == Enforce，Shadow 只写日志不形成可阻塞裁决；调用面契约源码不在本条会话改动范围。'
           DueWhen = '非 Enforce 路径写入可追溯失败台账并在阈值/严重度上可阻塞，或默认姿态转 Enforce。'
           Owner = '调用面契约' }

        # ── AV5 边准入：declared=0 / Permissive ──
        @{ Rule = 'AV5'; Subject = 'ReferenceEdgeCatalog/AgentStorageBundle'
           Reason = '声明边为空（declared=0）仅记录，Permissive 目录 IsDeclared 恒真；数据存储源码不在本条会话改动范围。'
           DueWhen = 'Declared 目录在 Count==0 时拒绝装配（throw），Permissive 仅测试可见或显式豁免。'
           Owner = 'DataStore 边准入' }

        # ── AV6 声明孤儿：默认 Report ──
        @{ Rule = 'AV6'; Subject = 'AgentConfigValue.DeclarationStrictnessDefault'
           Reason = '默认姿态 Report，366 个声明孤儿只报告不裁决；需逐条理由，尚未完成登记。'
           DueWhen = '366 孤儿逐条登记 Reason/DueWhen（declaration-exemptions.json 全覆盖）后仍可保留 Report，或默认转 Throw。'
           Owner = '声明闭合面' }

        # ── AV7 部署：存在 ≠ 被引用 ──
        @{ Rule = 'AV7'; Subject = 'DeploymentReport'
           Reason = '部署判据只有 Missing/Resolved（资产存在性），无「被引用」反查；部署报告源码不在本条会话改动范围。'
           DueWhen = '清单资产增加消费方反查（ReferencedBy/ConsumerRef），存在但无人引用判红。'
           Owner = '部署/扩展装载面' }

        # ── AV8 聚拢健康 + 封存：只记录 / 对象非空 ──
        @{ Rule = 'AV8'; Subject = 'AgentAssemblySegments.EStepSealAsync'
           Reason = 'GatherPoints_health 只记录 issues，封存判据是对象非空；装配段落源码不在本条会话改动范围。'
           DueWhen = 'issues>0 抛红或写入裁决字段，封存按 factsCount/allAttached 等内容判据裁决。'
           Owner = 'Agent 装配裁决（运行期）' }

        # ── AV9 VM 占位验证器 ──
        @{ Rule = 'AV9'; Subject = 'VmVerificationGate'
           Reason = 'NullVmVerifier 占位恒通过，VerifyAsync 未拒绝占位；Vm 源码不在本条会话改动范围。'
           DueWhen = 'VerifyAsync 对 IsPlaceholder 明确判否（或实现真验证器），调用方无法据此判过。'
           Owner = 'Vm 验证面' }
    )
}
