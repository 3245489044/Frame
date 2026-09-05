using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public static class SecurityRuleRegistry
    {
        private static readonly List<ISecurityRule> _defaultRules = new()
        {
            new FileOperationRule(),
            new ProcessRule(),
            new ReflectionRule(),
            new UnsafeCodeRule(),
            new DynamicRule(),
            new EnvironmentRule(),
            new RegistryRule(),
            new PInvokeRule()
        };

        public static IReadOnlyList<ISecurityRule> GetDefaultRules() =>
            _defaultRules.AsReadOnly();

        public static void RegisterRule(ISecurityRule rule)
        {
            if (rule != null)
                _defaultRules.Add(rule);
        }

        public static void RegisterRules(IEnumerable<ISecurityRule> rules)
        {
            if (rules != null)
                _defaultRules.AddRange(rules);
        }

        public static void ClearRules() => _defaultRules.Clear();
    }
}
