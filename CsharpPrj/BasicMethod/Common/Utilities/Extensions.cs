#nullable disable   
namespace Common.GraphMindStudio.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static class StringListProcessor
    {
        public static List<string> ProcessStringList(
            this List<string> strings,
            Func<string, string> processor,
            bool skipNulls = true)
        {
            if (strings == null) return new List<string>();

            return strings
                .Where(s => !skipNulls || s != null)
                .Select(processor)
                .Where(s => !skipNulls || s != null)
                .ToList();
        }

        public static List<string> AppendToEach(this List<string> strings, string suffix)
        {
            return strings.ProcessStringList(s => s + suffix);
        }

        public static List<string> PrependToEach(this List<string> strings, string prefix)
        {
            return strings.ProcessStringList(s => prefix + s);
        }
    }
    public static class DictionaryExtensions
    {
        public static Dictionary<TKey, TValue> DictionaryMerge<TKey, TValue>(
            this Dictionary<TKey, TValue> source,
            Dictionary<TKey, TValue> other,
            MergeStrategy strategy = MergeStrategy.Overwrite)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var result = new Dictionary<TKey, TValue>(source);

            if (other == null) return result;

            foreach (var kvp in other)
            {
                if (result.ContainsKey(kvp.Key))
                {
                    result[kvp.Key] = HandleConflict(result[kvp.Key], kvp.Value, strategy);
                }
                else
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            return result;
        }

        private static TValue HandleConflict<TValue>(
            TValue existingValue,
            TValue newValue,
            MergeStrategy strategy)
        {
            return strategy switch
            {
                MergeStrategy.KeepExisting => existingValue,
                MergeStrategy.Overwrite => newValue,
                _ => newValue
            };
        }
    }

    public enum MergeStrategy
    {
        Overwrite,
        KeepExisting,
        ThrowOnConflict
    }
}
