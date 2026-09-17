
using Modules.Pipeline;

namespace UniversalWorkflowFramework.Core.Diagnostics
{
    public class DiagnosticEngine<TItem>
    {
        private readonly List<ICheckRule<TItem>> _rules = new();

        public int RuleCount => _rules.Count;

        public IEnumerable<string> RuleNames => _rules.Select(r => r.Name);

        public DiagnosticEngine<TItem> RegisterRule(ICheckRule<TItem> rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            _rules.Add(rule);
            return this;
        }

        public DiagnosticEngine<TItem> RegisterRules(IEnumerable<ICheckRule<TItem>> rules)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            foreach (var rule in rules)
                RegisterRule(rule);

            return this;
        }

        public DiagnosticResult RunFullDiagnostics(IEnumerable<TItem> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            return DiagnosticRuleBatch.Run(items, _rules);
        }

        public DiagnosticResult RunSelectedDiagnostics(IEnumerable<TItem> items, IEnumerable<string> ruleNames)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (ruleNames == null)
                throw new ArgumentNullException(nameof(ruleNames));

            var nameSet = new HashSet<string>(ruleNames, StringComparer.OrdinalIgnoreCase);
            return DiagnosticRuleBatch.Run(items, _rules.Where(r => nameSet.Contains(r.Name)).ToList());
        }

        public void Clear()
        {
            _rules.Clear();
        }

        public bool RemoveRule(string ruleName)
        {
            var rule = _rules.FirstOrDefault(r =>
                r.Name.Equals(ruleName, StringComparison.OrdinalIgnoreCase));
            if (rule != null)
            {
                return _rules.Remove(rule);
            }
            return false;
        }
    }

    /// <summary>
    /// 诊断规则**批执行**（管线化重构 Q1-1）：批次项 = 规则集，项管线 = 单条规则执行 + 异常收集。
    /// <para>全量 / 按名两条入口共用本实施，消除此前各写一遍的 <c>foreach + try/catch</c>（重复逻辑收敛）。</para>
    /// <para>契约（与迁移前逐字一致）：执行序 = **注册序**（选中集亦然，按名过滤不重排）；单规则异常 →
    /// <c>规则 '名' 执行失败: {ex.Message}</c> 且**不中断后续规则**；<c>TotalItems</c> = 项数。</para>
    /// </summary>
    internal static class DiagnosticRuleBatch
    {
        internal static DiagnosticResult Run<TItem>(
            IEnumerable<TItem> items, IReadOnlyList<ICheckRule<TItem>> rules)
        {
            var scope = new DiagnosticRuleScope<TItem>(items.ToList(), rules);

            var batch = new ForEachPipeline<DiagnosticRuleScope<TItem>, ICheckRule<TItem>>(
                source => source.Rules,
                new DelegatePipeline<DiagnosticRuleScope<TItem>, DiagnosticRuleScope<TItem>>(
                    "DiagnosticRule",
                    syncWithCtx: (current, _) => current.ExecuteCurrentRule()),
                onItemBefore: (current, rule, _) => current.Current = rule,
                name: "RunDiagnosticRules",
                description: "诊断规则批执行（逐规则 + 异常收集；Q1-1）");

            batch.Execute(scope);
            return scope.Result;
        }
    }

    /// <summary>批执行上下文：项集 + 规则集 + 唯一结果收集器（<see cref="DiagnosticResult"/>）。</summary>
    internal sealed class DiagnosticRuleScope<TItem>(
        IReadOnlyList<TItem> items,
        IReadOnlyList<ICheckRule<TItem>> rules)
    {
        internal IReadOnlyList<TItem> Items { get; } = items;

        internal IReadOnlyList<ICheckRule<TItem>> Rules { get; } = rules;

        internal DiagnosticResult Result { get; } = new() { TotalItems = items.Count };

        /// <summary>当前规则（由批管线在逐项前置回调置入）。</summary>
        internal ICheckRule<TItem>? Current { get; set; }

        /// <summary>执行当前规则；异常按既有文案收集（**不中断批次**，对齐迁移前语义）。</summary>
        internal DiagnosticRuleScope<TItem> ExecuteCurrentRule()
        {
            var rule = Current!;
            try
            {
                rule.Execute(Items, Result);
            }
            catch (Exception ex)
            {
                Result.AddError($"规则 '{rule.Name}' 执行失败: {ex.Message}");
            }

            return this;
        }
    }
}
