using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BasicMethod.DecisionEngine
{
    #region 条件评估器

    public class ConditionEvaluator
    {
        private static readonly ConcurrentDictionary<string, TypeReflectionCache> _reflectionCache = new();

        public bool Evaluate<TContext>(string condition, TContext context)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return false;

            var expr = condition.Trim();

            var parts = SplitAndPreserveQuotes(expr);
            if (parts.Length > 1)
            {
                foreach (var part in parts)
                {
                    if (!EvaluateSingleCondition(part.Trim(), context))
                        return false;
                }
                return true;
            }

            return EvaluateSingleCondition(expr, context);
        }

        private static string[] SplitAndPreserveQuotes(string expression)
        {
            var parts = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuote = false;
            char quoteChar = '\0';

            for (int i = 0; i < expression.Length; i++)
            {
                char c = expression[i];

                if (inQuote)
                {
                    current.Append(c);
                    if (c == quoteChar)
                        inQuote = false;
                }
                else if (c == '"' || c == '\'')
                {
                    current.Append(c);
                    inQuote = true;
                    quoteChar = c;
                }
                else if (c == '&' && i + 1 < expression.Length && expression[i + 1] == '&')
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    i++;
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                parts.Add(current.ToString());

            return parts.ToArray();
        }

        private static bool EvaluateSingleCondition<TContext>(string expr, TContext context)
        {
            var nullMatch = Regex.Match(expr, @"^context\.(\w+)\s*(==|!=)\s*null$", RegexOptions.IgnoreCase);
            if (nullMatch.Success)
            {
                var key = nullMatch.Groups[1].Value;
                var op = nullMatch.Groups[2].Value;

                var found = TryGetContextValue(context, key, out var actualValue);

                return op switch
                {
                    "==" => found && actualValue == null,
                    "!=" => found && actualValue != null,
                    _ => false
                };
            }

            var valueMatch = Regex.Match(expr, @"^context\.(\w+)\s*(==|!=)\s*(.+)$", RegexOptions.IgnoreCase);
            if (valueMatch.Success)
            {
                var key = valueMatch.Groups[1].Value;
                var op = valueMatch.Groups[2].Value;
                var expectedValue = valueMatch.Groups[3].Value.Trim().Trim('"', '\'');

                var found = TryGetContextValue(context, key, out var actualValue);

                if (!found)
                {
                    return op switch
                    {
                        "==" => false,
                        "!=" => true,
                        _ => false
                    };
                }

                if (actualValue == null)
                {
                    return op switch
                    {
                        "==" => expectedValue == null,
                        "!=" => expectedValue != null,
                        _ => false
                    };
                }

                return op switch
                {
                    "==" => string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase),
                    "!=" => !string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
            }

            return false;
        }

        private static bool TryGetContextValue<TContext>(TContext context, string key, out string? value)
        {
            value = null;

            if (context is System.Collections.IDictionary dict)
            {
                if (dict.Contains(key))
                {
                    value = dict[key]?.ToString();
                    return true;
                }
                return false;
            }

            if (context is IReadOnlyDictionary<string, object?> readOnlyDict)
            {
                if (readOnlyDict.TryGetValue(key, out var val))
                {
                    value = val?.ToString();
                    return true;
                }
                return false;
            }

            var cache = GetOrCreateReflectionCache<TContext>();

            foreach (var prop in cache.Properties)
            {
                if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.GetValue(context)?.ToString();
                    return true;
                }
            }

            foreach (var field in cache.Fields)
            {
                if (string.Equals(field.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = field.GetValue(context)?.ToString();
                    return true;
                }
            }

            if (cache.ContainsKeyMethod != null)
            {
                var result = cache.ContainsKeyMethod.Invoke(context, new object[] { key });
                if (result is bool b && b)
                {
                    if (cache.TryGetMethod != null)
                    {
                        var genericMethod = cache.TryGetMethod.MakeGenericMethod(typeof(string));
                        var parameters = new object[] { key, null! };
                        var successResult = genericMethod.Invoke(context, parameters);
                        var success = successResult is bool bs && bs;
                        if (success)
                        {
                            value = parameters[1]?.ToString();
                            return true;
                        }
                    }
                    return true;
                }
            }

            return false;
        }

        private static TypeReflectionCache GetOrCreateReflectionCache<TContext>()
        {
            var type = typeof(TContext);
            return _reflectionCache.GetOrAdd(type.FullName ?? type.Name, _ =>
            {
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                var containsKeyMethod = type.GetMethod("ContainsKey", new[] { typeof(string) });
                var tryGetMethod = type.GetMethod("TryGet");

                return new TypeReflectionCache(properties, fields, containsKeyMethod, tryGetMethod);
            });
        }

        private sealed record TypeReflectionCache(
            PropertyInfo[] Properties,
            FieldInfo[] Fields,
            MethodInfo? ContainsKeyMethod,
            MethodInfo? TryGetMethod);
    }

    #endregion
}
