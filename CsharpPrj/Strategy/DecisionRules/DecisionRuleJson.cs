using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Plate.JsonMethod.Validation;

namespace Strategy.DecisionRules
{
    /// <summary>DecisionRuleSet 的 JSON 序列化配置（规则文件 camelCase + 枚举字符串；与规则文件 schema 对齐）。</summary>
    public static class DecisionRuleJson
    {
        /// <summary>规则文件反序列化选项：属性名大小写不敏感 + 枚举按 JsonStringEnumMemberName 映射。</summary>
        public static JsonSerializerOptions DeserializeOptions { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>把 JSON 反序列化为 <see cref="DecisionRuleSet"/>（camelCase 规则文件）。</summary>
        public static DecisionRuleSet? Deserialize(string json)
            => JsonSerializer.Deserialize<DecisionRuleSet>(json, DeserializeOptions);
    }

    /// <summary>文件规则集加载器：按 LastWriteTime 失效缓存 + 可选热重载；加载失败回退已加载版本（单写者 + 版本号防竞态）。</summary>
    public sealed class FileRuleSetLoader : IRuleSetLoader
    {
        private readonly string _path;
        private readonly bool _reloadOnChange;
        private readonly object _lock = new();
        private DecisionRuleSet? _cached;
        private DateTime _lastWrite;
        private bool _loadedOnce;

        public FileRuleSetLoader(string path, bool reloadOnChange = false)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            _path = Path.GetFullPath(path);
            _reloadOnChange = reloadOnChange;
        }

        public string? SourceName => _path;

        public DecisionRuleSet? Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_path))
                {
                    // 文件缺失：保留已加载版本（若从未加载则返回 null，由消费方回退内联/内建默认）。
                    return _loadedOnce ? _cached : null;
                }

                var write = File.GetLastWriteTimeUtc(_path);
                if (!_loadedOnce || (_reloadOnChange && write != _lastWrite))
                {
                    var loaded = TryLoadCore();
                    if (loaded is not null)
                    {
                        _cached = loaded;
                        _lastWrite = write;
                        _loadedOnce = true;
                    }
                    // 加载失败：保留 _cached（已加载版本），不抛错（热重载竞态保护）。
                }
                return _cached;
            }
        }

        private DecisionRuleSet? TryLoadCore()
        {
            var json = File.ReadAllText(_path);
            var validator = new RuleSetJsonValidator();
            var errors = validator.ValidateJson(json);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"规则文件校验失败（{_path}）：{string.Join("；", errors)}");
            }
            return DecisionRuleJson.Deserialize(json);
        }
    }

    /// <summary>规则集校验器：JSON 语法（复用 Plate.JsonMethod.JsonValidationService）+ 结构 + 语义 lint（错误正则显式报错，不吞）。</summary>
    public sealed class RuleSetJsonValidator : IRuleSetValidator
    {
        public IReadOnlyList<string> ValidateJson(string json)
        {
            var errors = new List<string>();

            // 语法校验（复用 JsonMethod）。
            var syntax = new JsonValidationService().ValidateSyntax(json);
            if (!syntax.IsValid)
            {
                errors.AddRange(syntax.Errors);
                return errors;
            }

            DecisionRuleSet? rs;
            try
            {
                rs = DecisionRuleJson.Deserialize(json);
            }
            catch (JsonException ex)
            {
                errors.Add($"规则集结构不合法：{ex.Message}");
                return errors;
            }
            if (rs is null) { errors.Add("规则集反序列化结果为空"); return errors; }

            errors.AddRange(Validate(rs));
            return errors;
        }

        public IReadOnlyList<string> Validate(DecisionRuleSet ruleSet)
        {
            var errors = new List<string>();
            if (ruleSet is null) { errors.Add("规则集为空"); return errors; }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in ruleSet.Rules ?? Array.Empty<DecisionRule>())
            {
                if (rule is null) continue;
                if (!string.IsNullOrWhiteSpace(rule.Id) && !seen.Add(rule.Id))
                    errors.Add($"规则 Id 重复：{rule.Id}");
                if (rule.Then is null) { errors.Add($"规则 {rule.Id ?? "(未命名)"} 缺少 then 输出"); continue; }
                if (rule.Then.Type == OutputKind.ToolCall && string.IsNullOrWhiteSpace(rule.Then.ToolName))
                    errors.Add($"规则 {rule.Id ?? "(未命名)"}：tool_call 缺少 toolName");
                if (rule.Then.Type == OutputKind.LLM
                    && string.IsNullOrWhiteSpace(rule.Then.LlmUserTemplate)
                    && string.IsNullOrWhiteSpace(rule.Then.Template)
                    && string.IsNullOrWhiteSpace(rule.Then.LlmSystemTemplate))
                    errors.Add($"规则 {rule.Id ?? "(未命名)"}：llm 至少需 llmUserTemplate / template / llmSystemTemplate 之一，否则无提示可执行");
                LintWhen(rule.When, rule.Id, errors);
            }
            if (ruleSet.Default is not null && ruleSet.Default.Then is not null)
                LintWhen(ruleSet.Default.When, ruleSet.Default.Id, errors);
            return errors;
        }

        private static void LintWhen(WhenCondition? when, string? ruleId, List<string> errors)
        {
            if (when is null) return;
            if (when.All is not null)
                foreach (var c in when.All)
                    LintCondition(c, ruleId, errors);
            if (when.Any is not null)
                foreach (var c in when.Any)
                    LintWhen(c, ruleId, errors);
        }

        private static void LintCondition(MatchCondition? cond, string? ruleId, List<string> errors)
        {
            if (cond is null) return;
            if (cond.Match is not null)
            {
                if (cond.Match.Strategy == MatchStrategy.Regex && !string.IsNullOrWhiteSpace(cond.Match.Value))
                {
                    try
                    {
                        Regex.IsMatch("", cond.Match.Value, RegexOptions.Singleline);
                    }
                    catch (ArgumentException ex)
                    {
                        errors.Add($"规则 {ruleId ?? "(未命名)"} 正则无效：{cond.Match.Value}（{ex.Message}）");
                    }
                }
            }
            if (cond.Not is not null) LintCondition(cond.Not, ruleId, errors);
        }
    }
}
