@{
    # ===== EndpointKeyConsistency 门禁基线（单源；守卫 = Operation\DSL\Data\Check-EndpointKeyConsistency.ps1）=====
    # 语义：端点键一致性棘轮（只降不升）。Value = 违规键计数表（Key/Count）：
    #   键消失 ⇒ 门禁提示「基线可收紧」；新增键 ⇒ 立即判红（K3 数据迁移后清空本表）。
    Schema = 'gate-baseline-v1'
    Gate   = 'EndpointKeyConsistency'

    # 已知工作流集（重复 wf 判据载体；空/未登记 ⇒ 仅默认 agent-round）。
    KnownWorkflowIds = @{ Value = @('agent-round'); Origin = 'Derived'; DueWhen = '' }

    # G1 history_owns_task.source.Id ∉ 历史根派生端点（现存 = 三重前缀，K3 重键后应清空）
    G1 = @{ Origin = 'Derived'; DueWhen = 'K3 重键后应清空'; Value = @(
        @{ Key = 'history.history:242e98688161'; Count = 1 }
        @{ Key = 'history.history:554552925c24'; Count = 1 }
        @{ Key = 'history.history:2f7965361cc7'; Count = 1 }
        @{ Key = 'history.history:b0659797532d'; Count = 1 }
        @{ Key = 'history.history:beb4af102952'; Count = 1 }
    ) }

    # G2a cross_db_link 的 task 源不在 ur_tasks（实体不存在 = 幻影 task）
    G2a = @{ Origin = 'Derived'; DueWhen = 'K3 重键后应清空'; Value = @(
        @{ Key = 'session'; Count = 5 }
    ) }

    # G2b cross_db_link 的 task 源存在于 ur_tasks 但不在 history_owns_task.target（树不可达）
    G2b = @{ Origin = 'Derived'; DueWhen = 'K3 重键后应清空'; Value = @(
        @{ Key = 'history:242e98688161'; Count = 1 }
        @{ Key = 'history:554552925c24'; Count = 1 }
        @{ Key = 'history:2f7965361cc7'; Count = 1 }
        @{ Key = 'history:b0659797532d'; Count = 1 }
        @{ Key = 'history:beb4af102952'; Count = 1 }
    ) }

    # G3 端点 Id 不动点（history/session/node）——现存 = history 三重前缀，K3 后应清空
    G3 = @{ Origin = 'Derived'; DueWhen = 'K3 重键后应清空'; Value = @(
        @{ Key = 'history.history:242e98688161'; Count = 1 }
        @{ Key = 'history.history:554552925c24'; Count = 1 }
        @{ Key = 'history.history:2f7965361cc7'; Count = 1 }
        @{ Key = 'history.history:b0659797532d'; Count = 1 }
        @{ Key = 'history.history:beb4af102952'; Count = 1 }
    ) }

    # G4 session_owns.source 不动点——目标恒为空
    G4 = @{ Value = @(); Origin = 'Derived'; DueWhen = '恒为空' }
}
