
using System;
using System.Collections.Generic;
using System.Dynamic;

namespace WorkFlowMethod.Infrastructure
{
    public static class ExpandoObjectHelper
    {
        public static dynamic ConvertDictionaryToExpando(Dictionary<string, object> source)
        {
            dynamic expando = new ExpandoObject();
            var expandoDict = expando as IDictionary<string, object>;
            foreach (var kvp in source)
                expandoDict[kvp.Key] = kvp.Value;
            return expando;
        }

        public static string? GetStringValue(dynamic expando, string key)
        {
            var dict = expando as IDictionary<string, object>;
            if (dict != null && dict.TryGetValue(key, out var value) && value != null)
                return value.ToString();
            return null;
        }

        public static object? GetValue(dynamic expando, string key)
        {
            var dict = expando as IDictionary<string, object>;
            if (dict != null && dict.TryGetValue(key, out var value))
                return value;
            return null;
        }

        public static dynamic CreateExpando()
        {
            return new ExpandoObject();
        }

    }
}
