
using System.Text.Json;

namespace ParameterResolver.KeyMapping;

public abstract class KeyMapperBase<TKey> : Abstractions.IKeyMapper<TKey>
{
    #region IKeyMapper<TKey> 实现

    public TKey Map(TKey sourceKey, TKey targetKey, KeyMappingConfig config)
    {
        if (sourceKey == null || targetKey == null || config == null)
            return targetKey;

        OnBeforeMap(sourceKey, targetKey, config);

        var normalizedSourceKeys = NormalizeKeys(new[] { sourceKey }, config);
        var usedSourceKeys = new HashSet<int>();
        var sortedRules = config.Rules.OrderBy(r => r.Priority).ToList();
        var result = targetKey;

        foreach (var rule in sortedRules)
        {
            if (TryApplyRule(targetKey, normalizedSourceKeys, rule, config, usedSourceKeys, out var mappedKey))
            {
                result = mappedKey;
                break;
            }
        }

        OnAfterMap(sourceKey, targetKey, result, config);
        return result;
    }

    public (IReadOnlyList<TKey> UpdatedKeys, IReadOnlyList<KeyMatchResult> MatchResults)
        MapBatch(IReadOnlyList<TKey> sourceKeys, IReadOnlyList<TKey> targetKeys, KeyMappingConfig config)
    {
        if (sourceKeys == null || targetKeys == null || config == null)
            return (targetKeys?.ToList() ?? new List<TKey>(), Array.Empty<KeyMatchResult>());

        var updatedKeys = new List<TKey>(targetKeys);
        var matchResults = new List<KeyMatchResult>();

        if (sourceKeys.Count == 0)
            return (updatedKeys, matchResults);

        var normalizedSourceKeys = NormalizeKeys(sourceKeys, config);
        var usedSourceKeys = new HashSet<int>();
        var sortedRules = config.Rules.OrderBy(r => r.Priority).ToList();

        for (int i = 0; i < targetKeys.Count; i++)
        {
            var targetKey = targetKeys[i];
            foreach (var rule in sortedRules)
            {
                if (TryApplyRule(targetKey, normalizedSourceKeys, rule, config, usedSourceKeys, matchResults, out var mappedKey))
                {
                    updatedKeys[i] = mappedKey;
                    break;
                }
            }
        }

        return (updatedKeys, matchResults);
    }

    public virtual IReadOnlyList<TKey> ParseKeysFromJson(string keysJson)
    {
        if (string.IsNullOrEmpty(keysJson))
            return Array.Empty<TKey>();

        try
        {
            return JsonSerializer.Deserialize<List<TKey>>(keysJson) ?? new List<TKey>();
        }
        catch
        {
            return Array.Empty<TKey>();
        }
    }

    public virtual IReadOnlyList<TKey> NormalizeKeys(IReadOnlyList<TKey> keys, KeyMappingConfig config)
    {
        if (keys == null || keys.Count == 0)
            return Array.Empty<TKey>();

        if (!config.EnableHierarchyNormalization)
            return keys;

        return keys
            .Where(k => k != null && !IsEmptyKey(k))
            .Distinct()
            .Select(TrimKey)
            .ToList();
    }

    #endregion

    #region 模板方法（可重写）

    protected virtual void OnBeforeMap(TKey sourceKey, TKey targetKey, KeyMappingConfig config) { }

    protected virtual void OnAfterMap(TKey sourceKey, TKey targetKey, TKey result, KeyMappingConfig config) { }

    protected virtual bool IsEmptyKey(TKey key) => key == null || key.ToString() == string.Empty;

    protected virtual TKey TrimKey(TKey key) => key;

    protected virtual string KeyToString(TKey key) => key?.ToString() ?? string.Empty;

    protected virtual TKey StringToKey(string value) =>
        typeof(TKey) == typeof(string) ? (TKey)(object)value : throw new NotSupportedException($"Conversion from string to {typeof(TKey).Name} is not supported. Override StringToKey.");

    #endregion

    #region 映射引擎核心

    private bool TryApplyRule(
        TKey targetKey,
        IReadOnlyList<TKey> sourceKeys,
        MappingRule rule,
        KeyMappingConfig config,
        HashSet<int> usedSourceKeys,
        out TKey mappedKey)
    {
        return TryApplyRule(targetKey, sourceKeys, rule, config, usedSourceKeys, null, out mappedKey);
    }

    private bool TryApplyRule(
        TKey targetKey,
        IReadOnlyList<TKey> sourceKeys,
        MappingRule rule,
        KeyMappingConfig config,
        HashSet<int> usedSourceKeys,
        List<KeyMatchResult>? matchResults,
        out TKey mappedKey)
    {
        mappedKey = targetKey;
        var targetStr = KeyToString(targetKey);

        for (int si = 0; si < sourceKeys.Count; si++)
        {
            if (!rule.MatchAllSourceKeys && usedSourceKeys.Contains(si))
                continue;

            var sourceStr = KeyToString(sourceKeys[si]);

            if (TryMatchRule(targetStr, sourceStr, rule, config, out var matchResult))
            {
                if (matchResults != null)
                {
                    matchResult.AppliedRuleName = $"Rule_{rule.RuleType}_{rule.Priority}";
                    matchResults.Add(matchResult);
                }
                if (!rule.MatchAllSourceKeys)
                    usedSourceKeys.Add(si);
                mappedKey = StringToKey(matchResult.MappedKey);
                return true;
            }
        }

        return false;
    }

    private bool TryMatchRule(
        string targetKey,
        string sourceKey,
        MappingRule rule,
        KeyMappingConfig config,
        out KeyMatchResult matchResult)
    {
        matchResult = null;
        try
        {
            return rule.RuleType switch
            {
                MappingRuleType.Exact => TryExactMatch(targetKey, sourceKey, rule, out matchResult),
                MappingRuleType.Pattern => TryPatternMatch(targetKey, sourceKey, rule, config, out matchResult),
                _ => TryHierarchicalMatch(targetKey, sourceKey, rule, config, out matchResult)
            };
        }
        catch (Exception ex)
        {
            throw new Exception($"规则匹配异常: {rule.RuleType}, 目标: {targetKey}, 源: {sourceKey}, 错误: {ex.Message}");
        }
    }

    #endregion

    #region 匹配实现

    private static bool TryExactMatch(string targetKey, string sourceKey, MappingRule rule, out KeyMatchResult matchResult)
    {
        matchResult = null;
        if (targetKey != rule.TargetPattern || sourceKey != rule.SourcePattern)
            return false;

        matchResult = KeyMatchResult.Create(sourceKey, targetKey, rule.TargetPattern, MappingRuleType.Exact.ToString());
        return true;
    }

    private static bool TryPatternMatch(string targetKey, string sourceKey, MappingRule rule, KeyMappingConfig config, out KeyMatchResult matchResult)
    {
        matchResult = null;
        if (!PathProcessor.IsPatternMatch(targetKey, rule.TargetPattern, config.WildcardChar) ||
            !PathProcessor.IsPatternMatch(sourceKey, rule.SourcePattern, config.WildcardChar))
            return false;

        string mappedKey;
        if (rule.TargetPattern.Contains(config.WildcardChar))
        {
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(rule.TargetPattern)
                .Replace("\\" + config.WildcardChar, "(.*)") + "$";
            var match = System.Text.RegularExpressions.Regex.Match(targetKey, regexPattern);
            mappedKey = match.Success && match.Groups[1].Success
                ? rule.TargetPattern.Replace(config.WildcardChar, match.Groups[1].Value)
                : targetKey;
        }
        else
        {
            mappedKey = rule.TargetPattern;
        }

        matchResult = KeyMatchResult.Create(sourceKey, targetKey, mappedKey, MappingRuleType.Pattern.ToString());
        matchResult.IsPartialMatch = rule.TargetPattern.Contains(config.WildcardChar) || rule.SourcePattern.Contains(config.WildcardChar);
        return true;
    }

    private static bool TryHierarchicalMatch(string targetKey, string sourceKey, MappingRule rule, KeyMappingConfig config, out KeyMatchResult matchResult)
    {
        matchResult = null;
        if (rule.LevelPatterns.Count == 0)
            return false;

        var separator = GetSeparator(rule, config);
        var targetLevels = PathProcessor.SplitKeyParts(targetKey, separator);
        var sourceLevels = PathProcessor.SplitKeyParts(sourceKey, separator);

        if (rule.RuleType == MappingRuleType.MultiLevel && rule.RequireAllLevels && targetLevels.Length != sourceLevels.Length)
            return false;

        var levelMatches = FindLevelMatches(targetLevels, sourceLevels, rule, config);
        if (levelMatches != null && levelMatches.Count > 0)
        {
            if (!CheckRequiredMappings(levelMatches, rule))
                return false;

            string mappedKey = BuildKeyBySeparator(targetLevels, sourceLevels, levelMatches, separator);
            var matchedLevels = levelMatches.Select(m => $"{targetLevels[m.Key]}->{sourceLevels[m.Value]}").ToList();

            matchResult = KeyMatchResult.Create(sourceKey, targetKey, mappedKey, rule.RuleType.ToString());
            matchResult.MatchedLevels = matchedLevels;
            matchResult.IsPartialMatch = levelMatches.Count < (rule.RequireAllLevels ? rule.LevelPatterns.Count : targetLevels.Length);
            return true;
        }

        return false;
    }

    private static Dictionary<int, int> FindLevelMatches(
        string[] targetLevels,
        string[] sourceLevels,
        MappingRule rule,
        KeyMappingConfig config)
    {
        var levelMatches = new Dictionary<int, int>();
        var usedSourceIndices = new HashSet<int>();
        int matchedCount = 0;

        foreach (var mapping in rule.LevelPatterns)
        {
            bool matched = false;
            for (int ti = 0; ti < targetLevels.Length && !matched; ti++)
            {
                if (levelMatches.ContainsKey(ti)) continue;
                if (!PathProcessor.IsPatternMatch(targetLevels[ti], mapping.TargetPattern, config.WildcardChar)) continue;

                for (int si = 0; si < sourceLevels.Length; si++)
                {
                    if (usedSourceIndices.Contains(si)) continue;
                    if (PathProcessor.IsPatternMatch(sourceLevels[si], mapping.SourcePattern, config.WildcardChar))
                    {
                        levelMatches[ti] = si;
                        usedSourceIndices.Add(si);
                        matchedCount++;
                        matched = true;
                        break;
                    }
                }
            }

            if (!matched && mapping.IsRequired && rule.RuleType == MappingRuleType.MultiLevel)
                return null;
        }

        bool conditionMet = rule.RuleType == MappingRuleType.MultiLevel
            ? (rule.RequireAllLevels ? matchedCount == rule.LevelPatterns.Count : matchedCount > 0)
            : matchedCount > 0;

        return conditionMet ? levelMatches : null;
    }

    #endregion

    #region 辅助方法

    private static string GetSeparator(MappingRule rule, KeyMappingConfig config) =>
        !string.IsNullOrEmpty(rule.LevelSeparator) ? rule.LevelSeparator : config.DefaultSeparator;

    private static bool CheckRequiredMappings(Dictionary<int, int> levelMatches, MappingRule rule)
    {
        if (rule.RuleType != MappingRuleType.MultiLevel) return true;
        var requiredMappings = rule.LevelPatterns.Count(m => m.IsRequired);
        return levelMatches.Count >= requiredMappings;
    }

    private static string BuildKeyBySeparator(
        string[] targetLevels,
        string[] sourceLevels,
        Dictionary<int, int> levelMatches,
        string separator)
    {
        var parts = new List<string>();
        for (int ti = 0; ti < targetLevels.Length; ti++)
        {
            if (levelMatches.TryGetValue(ti, out int si))
                parts.Add(sourceLevels[si]);
            else
                parts.Add(targetLevels[ti]);
        }
        return string.Join(separator, parts);
    }

    #endregion
}
