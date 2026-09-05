
using Modules.Framework.Abstractions;

namespace Modules.Framework
{
    public class ConfigValidator<TConfig> : IConfigValidator<TConfig>
        where TConfig : class
    {
        private readonly List<Func<TConfig, ConfigValidationResult>> _rules = new();

        public ConfigValidator<TConfig> AddRule(Func<TConfig, ConfigValidationResult> rule)
        {
            _rules.Add(rule ?? throw new ArgumentNullException(nameof(rule)));
            return this;
        }

        public ConfigValidator<TConfig> Required(string propertyName, Func<TConfig, object?> getValue)
        {
            return AddRule(config =>
            {
                var result = new ConfigValidationResult();
                var value = getValue(config);
                if (value == null || (value is string str && string.IsNullOrEmpty(str)))
                {
                    result.AddError($"属性 '{propertyName}' 是必需的");
                }
                return result;
            });
        }

        public ConfigValidationResult Validate(TConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var result = new ConfigValidationResult();

            foreach (var rule in _rules)
            {
                var ruleResult = rule(config);
                result.Merge(ruleResult);
            }

            return result;
        }
    }
}
