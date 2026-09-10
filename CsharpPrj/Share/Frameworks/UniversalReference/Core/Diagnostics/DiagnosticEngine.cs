
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

            var result = new DiagnosticResult();
            var itemList = items.ToList();
            result.TotalItems = itemList.Count;

            foreach (var rule in _rules)
            {
                try
                {
                    rule.Execute(itemList, result);
                }
                catch (Exception ex)
                {
                    result.AddError($"规则 '{rule.Name}' 执行失败: {ex.Message}");
                }
            }

            return result;
        }

        public DiagnosticResult RunSelectedDiagnostics(IEnumerable<TItem> items, IEnumerable<string> ruleNames)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (ruleNames == null)
                throw new ArgumentNullException(nameof(ruleNames));

            var result = new DiagnosticResult();
            var itemList = items.ToList();
            result.TotalItems = itemList.Count;
            var nameSet = new HashSet<string>(ruleNames, StringComparer.OrdinalIgnoreCase);

            foreach (var rule in _rules.Where(r => nameSet.Contains(r.Name)))
            {
                try
                {
                    rule.Execute(itemList, result);
                }
                catch (Exception ex)
                {
                    result.AddError($"规则 '{rule.Name}' 执行失败: {ex.Message}");
                }
            }

            return result;
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
}
