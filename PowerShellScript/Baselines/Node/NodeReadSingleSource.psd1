@{
    # ===== NodeReadSingleSource 门禁基线（单源；守卫 = Modules\Node\Check-NodeReadSingleSource.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'NodeReadSingleSource'

    # 端点归一生产调用文件数（唯一实现 = NodeKeySetResolver；树保留声明流回退一处）。
    NormalizeCalls = @{ Value = 2; Origin = 'Derived'; DueWhen = 'B4 收口后降为 1（声明流回退迁移到 Resolver）' }

    # 任务节点读面调用文件数（棘轮只降不升；B3–B5 逐消费方收口）。
    TaskNodeReadCalls = @{ Value = 5; Origin = 'Derived'; DueWhen = 'B3–B5 收口后下调' }

    # 投影生产调用文件数（I1；唯一入口 = NodeReadService）。
    ProjectionCallSites = @{ Value = 1; Origin = 'Derived'; DueWhen = '恒为 1（只降不升）' }

    # 全量读显式声明点（I2；白名单上限）。
    FullScopeSites = @{ Value = 3; Origin = 'Derived'; DueWhen = '库页/诊断之外不得出现' }

    # 原因词表数（I5；框架枚举 + Agent 禁止第二套裸词）。
    ReasonVocabularies = @{ Value = 1; Origin = 'Derived'; DueWhen = '恒为 1（只降不升）' }
}
