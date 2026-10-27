@{
    # ===== ReadModelSingleSource 门禁基线（单源；守卫 = Operation\DSL\Process\Check-ReadModelSingleSource.ps1）=====
    # 语义（读模型方案 §12）：读模型声明是「模型 id → 源键 + 投影器 + 版本源」的唯一生产者；
    #   判据载体 = 生成目录 JSON 结构事实 + 源码静态计数（SEP.JudgmentCarrierStructural）。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'read-model-baseline-v1'
    Gate   = 'ReadModelSingleSource'

    # 生成目录（op.model 与 readModels 段的唯一读取面）。
    Catalog = @{ Value = 'Projects\Plug\Agent\Config\commands\ui-commands.generated.json'; Origin = 'Derived'; DueWhen = '' }

    # 声明条数（RM-B/C 已迁移 conversation.tree + cost.badge ⇒ 2）。
    Declarations = @{ Value = 2; Origin = 'Derived'; DueWhen = 'RM-D/E 起随声明递增' }

    # 投影实现数（= 声明条数；RM-B/C = 2）。
    Projectors = @{ Value = 2; Origin = 'Derived'; DueWhen = '' }

    # 视图侧缓存形态计数上限（RM-B/C 删除会话树缓存/成本 TTL 后实测 = 0；棘轮只降不升）。
    ViewCacheSites = @{ Value = 0; Origin = 'Debt'; DueWhen = '' }

    # 版本源词表（声明处选定；projector 自带版本 ⇒ 红）。
    VersionModes = @{ Value = @('None', 'Ttl', 'Event', 'GraphMembership'); Origin = 'Derived'; DueWhen = '' }
}
