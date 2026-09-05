using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public class StaticCodeChecker
    {
        private readonly IReadOnlyList<ISecurityRule> _rules;

        public StaticCodeChecker() : this(SecurityRuleRegistry.GetDefaultRules()) { }

        public StaticCodeChecker(IReadOnlyList<ISecurityRule> rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public async Task<SecurityCheckResult> CheckAsync(string code)
        {
            return await SecurityAnalysisSkeleton.AnalyzeAsync(code, _rules);
        }

        public SecurityCheckResult Check(string code)
        {
            return SecurityAnalysisSkeleton.Analyze(code, _rules);
        }

        public IReadOnlyList<ISecurityRule> GetRules() => _rules;
    }
}
