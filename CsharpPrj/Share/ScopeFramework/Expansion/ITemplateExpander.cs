namespace Share.ScopeFramework;

/// <summary>
/// 通用"模板 → 线性可执行序列"展开契约（策略 C：Share 层通用接口，ScopeCommandTranslator 与 GraphKey 各实现一份，
/// 消除两套展开管线的设计重复）。权威：任务中心多层作用域封装框架设计 §4.6 策略 C。
/// 数据流模型差异（plan §4.6）：作用域=扇出式、GraphKey=链式且执行在 expander 之外，故共享部分是"展开为线性序列（dry-run）"本身，
/// 执行循环各自持有、不定义在此接口（避免为 GraphKey 发明不存在的执行语义）。
/// </summary>
public interface ITemplateExpander<TTemplate, TStep>
{
    /// <summary>将模板展开为线性步骤/节点序列（dry-run，不执行）。</summary>
    IReadOnlyList<TStep> Expand(TTemplate template);
}
