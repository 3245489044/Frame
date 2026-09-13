
using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Abstractions;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.DefaultImplementations
{
    /// <summary>
    /// 规则链阶段：对**根节点**依次尝试规则，首个命中即采用其产物（<c>firstMatchOnly</c>=false 则串行叠加）。
    /// 无规则命中时原样透传，不产生诊断。
    /// </summary>
    public sealed class RuleChainStage : IConversionStage
    {
        private readonly List<IConversionRule> _rules;
        private readonly bool _firstMatchOnly;

        public RuleChainStage(string name, IEnumerable<IConversionRule> rules, bool firstMatchOnly = true)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("阶段名不能为空", nameof(name));
            if (rules is null) throw new ArgumentNullException(nameof(rules));

            Name = name;
            _rules = new List<IConversionRule>(rules);
            _firstMatchOnly = firstMatchOnly;
        }

        public string Name { get; }

        public IReadOnlyList<IConversionRule> Rules => _rules;

        public ConversionNode Execute(ConversionNode input, ConversionContext context)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (context is null) throw new ArgumentNullException(nameof(context));

            var current = input;

            for (var i = 0; i < _rules.Count; i++)
            {
                var rule = _rules[i];

                if (!rule.TryApply(current, context, out var result, out var diagnostic))
                    continue;

                if (diagnostic is not null) context.Diagnostics.Report(diagnostic);

                if (result is null)
                {
                    context.Diagnostics.Report(ConversionDiagnostic.Error(
                        "CVT-RULE-NULL",
                        $"规则 \"{rule.Name}\" 命中但返回 null（已保留原节点）",
                        context.SpanAt(0, 0)));
                    continue;
                }

                current = result;
                if (_firstMatchOnly) break;
            }

            return current;
        }
    }
}
