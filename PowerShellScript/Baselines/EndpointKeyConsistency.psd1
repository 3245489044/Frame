@{
    Schema  = 'endpoint-key-consistency-v2'
    Origin  = 'Derived'
    DueWhen = 'K3 数据迁移执行后清空本表（只降不升；门禁会提示可收紧项）'

    # 已知工作流集（重复 wf 判据载体；空/未登记 ⇒ 仅默认 agent-round）。
    KnownWorkflowIds = @('agent-round')

    # G1 history_owns_task.source.Id ∉ 历史根派生端点（现存 = 三重前缀，K3 重键后应清空）
    G1 = @(
        @{ Key = 'history.history:242e98688161'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history.history:554552925c24'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history.history:2f7965361cc7'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history.history:b0659797532d'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history.history:beb4af102952'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
    )

    # G2a cross_db_link 的 task 源不在 ur_tasks（实体不存在 = 幻影 task）
    G2a = @()

    # G2b cross_db_link 的 task 源存在于 ur_tasks 但不在 history_owns_task.target（树不可达）
    G2b = @(
        @{ Key = 'history:242e98688161'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history:554552925c24'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history:2f7965361cc7'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history:b0659797532d'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history:beb4af102952'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'session'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
    )

    # G3 端点 Id 不动点（history/session/node）——现存 = history 三重前缀，K3 后应清空
    G3 = @(
        @{ Key = 'history.history:242e98688161'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history.history:554552925c24'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history.history:2f7965361cc7'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history.history:b0659797532d'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
        @{ Key = 'history.history:beb4af102952'; Count = 1; Origin = 'Derived'; DueWhen = 'K3' }
    )

    # G4 session_owns.source 不动点——目标恒为空
    G4 = @()
}
