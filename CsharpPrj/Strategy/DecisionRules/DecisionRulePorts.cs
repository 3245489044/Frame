using System.Collections.Generic;

namespace Strategy.DecisionRules
{
    /// <summary>匹配器端口：新增匹配 = 注册一个实现，不改核心（Open-Closed）。</summary>
    public interface IMatchStrategy
    {
        MatchStrategy Kind { get; }

        /// <summary>归一精确度（exact &gt; jsonPath &gt; wildcard &gt; regex &gt; contains &gt; role &gt; always），declared 模式裁决用。</summary>
        int Specificity { get; }

        bool Match(MatchClause clause, DecisionRuleContext ctx, bool caseSensitive);
    }

    /// <summary>输出器端口：新增输出类型 = 注册一个实现。</summary>
    public interface IOutputStrategy
    {
        OutputKind Kind { get; }

        object? Render(OutputClause clause, DecisionRuleContext ctx, ITemplateRenderer renderer);
    }

    /// <summary>占位符模板渲染端口（{system}/{user}/{history}/{index}/{messages}/{last_assistant}/{last_message}/{role:&lt;名&gt;}/{context.&lt;key&gt;}）。</summary>
    public interface ITemplateRenderer
    {
        string Render(string? template, DecisionRuleContext ctx);
    }

    /// <summary>规则集加载端口（文件/内联/内存）。</summary>
    public interface IRuleSetLoader
    {
        string? SourceName { get; }

        DecisionRuleSet? Load();
    }

    /// <summary>规则集校验端口（JSON 语法/结构 + 语义 lint）。</summary>
    public interface IRuleSetValidator
    {
        IReadOnlyList<string> Validate(DecisionRuleSet ruleSet);

        /// <summary>对规则文件原始 JSON 做语法/结构校验（错误正则等显式报错，不吞）。</summary>
        IReadOnlyList<string> ValidateJson(string json);
    }

    /// <summary>匹配策略注册表：引擎经此取策略实现。</summary>
    public interface IMatchStrategyRegistry
    {
        IMatchStrategy Get(MatchStrategy strategy);
    }

    /// <summary>输出器注册表：引擎经此取输出器实现。</summary>
    public interface IOutputStrategyRegistry
    {
        IOutputStrategy Get(OutputKind kind);
    }

    /// <summary>命中明细审计端口（记录命中规则 id / 匹配字段 / 输出类型 / 兜底与否）。</summary>
    public interface IRuleAuditSink
    {
        void Record(RuleAuditEntry entry);
    }

    /// <summary>审计条目。</summary>
    public sealed record RuleAuditEntry(
        string? RuleSetId,
        string? RuleId,
        string? Field,
        string? OutputKind,
        bool IsFallback,
        string? Source = null);
}
