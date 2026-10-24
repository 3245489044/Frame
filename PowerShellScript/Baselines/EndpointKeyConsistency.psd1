@{
    Schema  = 'endpoint-key-consistency-v1'
    Origin  = 'Derived'
    DueWhen = 'K3 数据迁移执行后清空本表（只降不升；门禁会提示可收紧项）'

    # G1 history_owns_task.source.Id ∉ 历史根派生端点（现存 = 三重前缀，K3 重键后应清空）
    G1 = @(
        'history.history:242e98688161'
        'history.history:554552925c24'
        'history.history:2f7965361cc7'
        'history.history:b0659797532d'
        'history.history:beb4af102952'
    )

    # G2 cross_db_link 的 task 源 ∉ 树可达成员 task（会话任务 history:* 待 K3 合并；session 为幻影端点）
    G2 = @(
        'history:242e98688161'
        'history:554552925c24'
        'history:2f7965361cc7'
        'history:b0659797532d'
        'history:beb4af102952'
        'session'
    )

    # G3 端点 Id 不动点（history/session/node）——现存 = history 三重前缀，K3 后应清空
    G3 = @(
        'history.history:242e98688161'
        'history.history:554552925c24'
        'history.history:2f7965361cc7'
        'history.history:b0659797532d'
        'history.history:beb4af102952'
    )

    # G4 session_owns.source 不动点——目标恒为空
    G4 = @()
}
