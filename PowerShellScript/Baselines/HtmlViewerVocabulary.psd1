@{
    # ===== HtmlViewerVocabulary 门禁基线（单源；守卫 = Operation\DSL\Check-HtmlViewerVocabulary.ps1）=====
    # 语义：框架（Plate.HtmlViewer）只描述结构词汇（壳/分栏/面板/列表/项/行/分组/折叠/分段/状态）；
    #       领域语义（history/task/node/session/message/...）只允许出现在 ①配置 JSON 键值 ②Agent 数据模型 ③域样式注入。
    # 权威：CsharpPrj\Plate\HtmlViewer\doc\HTML模板去领域语义-通用化重构方案.md（§2 正/负词表）。
    Schema = 'gate-baseline-v1'
    Gate   = 'HtmlViewerVocabulary'

    # 豁免计数（下限事实；零豁免 ⇒ 新增语义只能走配置模板覆盖 / RegisterStyleModule / 域数据模型）
    AllowCount = @{ Value = 0; Origin = 'Derived'; DueWhen = '常驻：词汇门禁零豁免（如需登记须同步 Check-HtmlViewerVocabulary.ps1 的 Allow 表并注明理由）' }
}
