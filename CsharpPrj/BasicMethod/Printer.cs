using Microsoft.CodeAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
#nullable disable

namespace GraphMindStudio.Print
{
    public static class Printer
    {
        #region ����API
        public static string PrintCollectionList(List<object> collection, List<string> separators = null, bool reflection = false, PrintOptions options = null)
        {
            string result = null;
            for (int i = 0; i < collection.Count; i++)
            {
                string sep = separators != null && i < separators.Count ? separators[i] : null;
                result += PrintCollection(collection[i], sep, reflection, options);
            }
            return result;
        }
        public static string PrintCollection(object collection, string separator = "", bool reflection = false, PrintOptions options = null)
        {
            options ??= PrintOptions.Default;
            return OutputFormatter.FormatResultWithSeparator(
                PrinterCore.PrintObjectInternal(collection, reflection, options, 0, new HashSet<object>(ReferenceEqualityComparer.Instance)),
                separator);
        }

        public static string PrintMultipleCollections(string separator = "", bool reflection = false,
                                                    PrintOptions options = null, params object[] collections)
        {
            if (collections == null || collections.Length == 0)
                return OutputFormatter.FormatResultWithSeparator(string.Empty, separator);

            options ??= PrintOptions.Default;
            var results = collections
                .Where(c => c != null)
                .Select(c => PrinterCore.PrintObjectInternal(c, reflection, options, 0, new HashSet<object>(ReferenceEqualityComparer.Instance)))
                .ToList();

            return results.Count == 0
                ? OutputFormatter.FormatResultWithSeparator(string.Empty, separator)
                : OutputFormatter.FormatMultipleResultsWithSeparator(results, separator);
        }
        #endregion

        #region Debug����
        public static void DebugPrint(object collection, bool reflection = false, string separator = "", PrintOptions options = null) =>
        Console.WriteLine(PrintCollection(collection, separator, reflection, options));
        public static void DebugListPrint(List<object> collection, bool reflection = false, List<string> separators = null, PrintOptions options = null) =>
          Console.WriteLine(PrintCollectionList(collection, separators, reflection, options));

        public static void DebugPrintCollections(bool reflection = false, string separator = "", PrintOptions options = null, params object[] collections) =>
           Console.WriteLine(PrintMultipleCollections(separator, reflection, options, collections));

        public static void DebugCompare(object input, object output, bool reflection = false, string separator = null, PrintOptions options = null)
        {
            var inputStr = PrintCollection(input, "INPUT:", reflection, options);
            var outputStr = PrintCollection(output, "OUTPUT:", reflection, options);
            Console.WriteLine(OutputFormatter.FormatComparisonResult(inputStr, outputStr));
        }
        #endregion

        #region PrintOptions ��
        public class PrintOptions
        {
            public int? MaxDepth { get; set; }
            public int? MaxItems { get; set; }
            public string[] IncludeProperties { get; set; }
            public string[] ExcludeProperties { get; set; }
            public bool ShowNullProperties { get; set; } = true;
            public bool ShowTypeInfo { get; set; } = true;
            public bool IncludeFields { get; set; } = true;
            public bool IncludePropertiesOnly { get; set; } = false;
            public bool ApplyFiltersToNestedObjects { get; set; } = true;
            public Func<Type, bool> TypeFilter { get; set; }
            public Func<MemberInfo, bool> MemberFilter { get; set; }

            public Func<object, bool> KeyFilter { get; set; }

            public static PrintOptions Default => new PrintOptions();

            public static PrintOptions OnlyProperties(params string[] properties) =>
                new PrintOptions { IncludePropertiesOnly = true, IncludeProperties = properties };

            public static PrintOptions Exclude(params string[] properties) =>
                new PrintOptions { ExcludeProperties = properties };

            public static PrintOptions Limit(int maxDepth, int maxItems) =>
                new PrintOptions { MaxDepth = maxDepth, MaxItems = maxItems };

            public static PrintOptions OnlyClassProperties(bool excludeCollections = true, bool excludePrimitives = true) =>
                new PrintOptions { MemberFilter = FilterFactory.CreateClassPropertiesFilter(excludeCollections, excludePrimitives) };

            public static PrintOptions OnlyNamespace(string namespacePrefix) =>
                new PrintOptions { MemberFilter = FilterFactory.CreateNamespaceFilter(namespacePrefix) };

            public PrintOptions WithMaxDepth(int? maxDepth) { MaxDepth = maxDepth; return this; }
            public PrintOptions WithMaxItems(int? maxItems) { MaxItems = maxItems; return this; }
            public PrintOptions WithIncludeProperties(params string[] properties) { IncludeProperties = properties; return this; }
            public PrintOptions WithExcludeProperties(params string[] properties) { ExcludeProperties = properties; return this; }
            public PrintOptions WithShowNullProperties(bool show) { ShowNullProperties = show; return this; }
            public PrintOptions WithShowTypeInfo(bool show) { ShowTypeInfo = show; return this; }
            public PrintOptions WithIncludeFields(bool include) { IncludeFields = include; return this; }
            public PrintOptions WithIncludePropertiesOnly(bool includeOnly) { IncludePropertiesOnly = includeOnly; return this; }
            public PrintOptions WithTypeFilter(Func<Type, bool> filter) { TypeFilter = filter; return this; }
            public PrintOptions WithMemberFilter(Func<MemberInfo, bool> filter) { MemberFilter = filter; return this; }
            public PrintOptions WithApplyFiltersToNestedObjects(bool apply) { ApplyFiltersToNestedObjects = apply; return this; }
            public PrintOptions WithKeyFilter(Func<object, bool> filter) { KeyFilter = filter; return this; }
        }
        #endregion

        #region ���Ĵ�ӡ�߼�
        internal static class PrinterCore
        {
            public static string PrintObjectInternal(object obj, bool reflection, PrintOptions options, int currentDepth, HashSet<object> visited)
            {
                var sb = new StringBuilder();
                PrintObject(obj, 0, sb, visited, reflection, options, currentDepth);
                return sb.ToString();
            }

            private static void PrintObject(object obj, int indentLevel, StringBuilder sb,
                                          HashSet<object> visited, bool reflection,
                                          PrintOptions options, int currentDepth)
            {
                if (obj == null)
                {
                    StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, "null");
                    return;
                }

                if (options.MaxDepth.HasValue && currentDepth >= options.MaxDepth.Value)
                {
                    StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, $"... (�������: {options.MaxDepth})");
                    return;
                }

                var type = obj.GetType();

                if (!type.IsValueType && type != typeof(string) && visited.Contains(obj))
                {
                    StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, "<ѭ������>");
                    return;
                }

                if (!type.IsValueType && type != typeof(string))
                    visited.Add(obj);

                if (obj is IDictionary dictionary)
                {
                    PrintDictionary(dictionary, indentLevel, sb, visited, reflection, options, currentDepth);
                }
                else if (obj is IEnumerable enumerable && type != typeof(string))
                {
                    PrintEnumerable(enumerable, indentLevel, sb, visited, reflection, options, currentDepth);
                }
                else if (TypeHelper.IsTupleType(type))
                {
                    PrintTuple(obj, indentLevel, sb, visited, reflection, options, currentDepth);
                }
                else if (reflection && type.IsClass && !typeof(IEnumerable).IsAssignableFrom(type))
                {
                    var nestedOptions = !options.ApplyFiltersToNestedObjects && currentDepth > 0
                        ? CreateNestedOptions(options)
                        : options;

                    PrintObjectByReflection(obj, indentLevel, sb, visited, nestedOptions, currentDepth);
                }
                else
                {
                    StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, obj.ToString());
                }

                if (!type.IsValueType && type != typeof(string))
                    visited.Remove(obj);
            }

            private static PrintOptions CreateNestedOptions(PrintOptions parentOptions)
            {
                return new PrintOptions
                {
                    MaxDepth = parentOptions.MaxDepth,
                    MaxItems = parentOptions.MaxItems,
                    ShowNullProperties = parentOptions.ShowNullProperties,
                    ShowTypeInfo = parentOptions.ShowTypeInfo,
                    IncludeFields = parentOptions.IncludeFields,
                    ApplyFiltersToNestedObjects = parentOptions.ApplyFiltersToNestedObjects
                };
            }

            #region �������ʹ�ӡʵ��
            private static void PrintDictionary(IDictionary dictionary, int indentLevel, StringBuilder sb,
                                               HashSet<object> visited, bool reflection, PrintOptions options, int currentDepth)
            {
                var count = dictionary.Count;
                var displayCount = OutputFormatter.GetDisplayCount(count, options.MaxItems);
                var hasLimit = options.MaxItems.HasValue && count > options.MaxItems.Value;

                StringBuilderHelper.AppendLineWithIndent(sb, indentLevel,
                    OutputFormatter.FormatDictionaryHeader(count, hasLimit, displayCount));
                StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, "{");

                int processed = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (options.KeyFilter != null && !options.KeyFilter(entry.Key))
                        continue;

                    if (CheckItemLimit(processed, count, options.MaxItems, indentLevel + 1, sb))
                        break;

                    StringBuilderHelper.AppendWithIndent(sb, indentLevel + 1, $"[{entry.Key}] = ");
                    PrintObject(entry.Value, 0, sb, visited, reflection, options, currentDepth + 1);
                    processed++;
                }

                StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, "}");
            }

            private static void PrintEnumerable(IEnumerable enumerable, int indentLevel, StringBuilder sb,
                                              HashSet<object> visited, bool reflection, PrintOptions options, int currentDepth)
            {
                try
                {
                    var items = new List<object>();
                    int count = 0;
                    var enumerator = enumerable.GetEnumerator();

                    while (enumerator.MoveNext())
                    {
                        items.Add(enumerator.Current);
                        count++;

                        if (options.MaxItems.HasValue && count > options.MaxItems.Value * 2)
                            break;
                    }

                    var displayCount = OutputFormatter.GetDisplayCount(count, options.MaxItems);
                    var hasLimit = options.MaxItems.HasValue && count > options.MaxItems.Value;

                    StringBuilderHelper.AppendLineWithIndent(sb, indentLevel,
                        OutputFormatter.FormatEnumerableHeader(count, hasLimit, displayCount));
                    StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, "[");

                    for (int i = 0; i < Math.Min(items.Count, displayCount); i++)
                    {
                        if (i > 0 && ShouldShowSeparator(items[i]))
                            StringBuilderHelper.AppendLineWithIndent(sb, indentLevel + 1, new string('-', 21));

                        PrintObject(items[i], indentLevel + 1, sb, visited, reflection, options, currentDepth + 1);
                    }

                    if (hasLimit && count > displayCount)
                        StringBuilderHelper.AppendLineWithIndent(sb, indentLevel + 1,
                            OutputFormatter.FormatOmittedItemsMessage(count, displayCount));

                    StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, "]");
                }
                catch (Exception ex)
                {
                    StringBuilderHelper.AppendLineWithIndent(sb, indentLevel,
                        $"<��������: {ex.GetType().Name}: {ex.Message}>");
                }
            }

            private static void PrintTuple(object tuple, int indentLevel, StringBuilder sb,
                                         HashSet<object> visited, bool reflection, PrintOptions options, int currentDepth)
            {
                var type = tuple.GetType();
                var fields = type.GetFields().Where(f => f.Name != "Rest").ToList();

                StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, $"Tuple ({fields.Count})");
                StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, "(");

                foreach (var field in fields)
                {
                    var value = field.GetValue(tuple);
                    if (!options.ShowNullProperties && value == null)
                        continue;

                    StringBuilderHelper.AppendWithIndent(sb, indentLevel + 1, $"{field.Name}: ");
                    PrintObject(value, 0, sb, visited, reflection, options, currentDepth + 1);
                }

                StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, ")");
            }

            private static void PrintObjectByReflection(object obj, int indentLevel, StringBuilder sb,
                                                      HashSet<object> visited, PrintOptions options, int currentDepth)
            {
                var type = obj.GetType();
                var typeInfo = options.ShowTypeInfo ? $"{type.Name} {{" : "{";
                StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, typeInfo);

                var members = TypeHelper.GetMembersToPrint(type, options);
                bool hasContent = false;

                foreach (var member in members)
                {
                    var value = TypeHelper.GetMemberValue(member, obj);
                    if (!options.ShowNullProperties && value == null)
                        continue;

                    StringBuilderHelper.AppendWithIndent(sb, indentLevel + 1, $"{member.Name}: ");

                    var nestedOptions = !options.ApplyFiltersToNestedObjects && currentDepth > 0
                        ? CreateNestedOptions(options)
                        : options;

                    PrintObject(value, 0, sb, visited, true, nestedOptions, currentDepth + 1);
                    hasContent = true;
                }

                if (!hasContent && members.Count == 0)
                {
                    var allMembers = TypeHelper.GetAllMembers(type, options);
                    StringBuilderHelper.AppendLineWithIndent(sb, indentLevel + 1,
                        allMembers.Count > 0
                            ? $"<���������ѱ����� (ԭʼ{allMembers.Count}������)>"
                            : "<û�пɷ��ʵĹ�������>");
                }

                StringBuilderHelper.AppendLineWithIndent(sb, indentLevel, "}");
            }
            #endregion

            #region ��������
            private static bool ShouldShowSeparator(object item)
            {
                if (item == null) return false;
                var type = item.GetType();
                return type.IsClass && type != typeof(string) && !type.IsPrimitive;
            }

            private static bool CheckItemLimit(int processed, int totalCount, int? maxItems, int indentLevel, StringBuilder sb)
            {
                if (maxItems.HasValue && processed >= maxItems.Value)
                {
                    StringBuilderHelper.AppendLineWithIndent(sb, indentLevel,
                        OutputFormatter.FormatOmittedItemsMessage(totalCount, maxItems.Value));
                    return true;
                }
                return false;
            }
            #endregion
        }
        #endregion

        #region ��������
        internal static class TypeHelper
        {
            public static bool IsTupleType(Type type) =>
                type.FullName != null &&
                (type.FullName.StartsWith("System.Tuple") ||
                 type.FullName.StartsWith("System.ValueTuple"));

            public static List<MemberInfo> GetMembersToPrint(Type type, PrintOptions options)
            {
                var members = GetAllMembers(type, options);
                return FilterMembers(members, options);
            }

            public static List<MemberInfo> GetAllMembers(Type type, PrintOptions options)
            {
                var members = new List<MemberInfo>();

                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead)
                    .Cast<MemberInfo>();

                members.AddRange(properties);

                if (options.IncludeFields && !options.IncludePropertiesOnly)
                {
                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .Cast<MemberInfo>();
                    members.AddRange(fields);
                }

                return members.OrderBy(m => m.Name).ToList();
            }

            private static List<MemberInfo> FilterMembers(List<MemberInfo> members, PrintOptions options)
            {
                if (members.Count == 0)
                    return members;

                IEnumerable<MemberInfo> result = members;

                if (options.IncludeProperties?.Length > 0)
                {
                    var includeSet = new HashSet<string>(options.IncludeProperties, StringComparer.OrdinalIgnoreCase);
                    result = result.Where(m => includeSet.Contains(m.Name));
                }

                if (options.ExcludeProperties?.Length > 0)
                {
                    var excludeSet = new HashSet<string>(options.ExcludeProperties, StringComparer.OrdinalIgnoreCase);
                    result = result.Where(m => !excludeSet.Contains(m.Name));
                }

                if (options.TypeFilter != null)
                {
                    result = result.Where(m =>
                    {
                        var memberType = GetMemberType(m);
                        return memberType != null && options.TypeFilter(memberType);
                    });
                }

                if (options.MemberFilter != null)
                {
                    result = result.Where(options.MemberFilter);
                }

                return result
                    .GroupBy(m => m.Name)
                    .Select(g => g.First())
                    .OrderBy(m => m.Name)
                    .ToList();
            }

            public static object GetMemberValue(MemberInfo member, object obj)
            {
                try
                {
                    return member switch
                    {
                        PropertyInfo prop => prop.GetValue(obj),
                        FieldInfo field => field.GetValue(obj),
                        _ => "<δ֪��Ա����>"
                    };
                }
                catch (Exception ex)
                {
                    return $"<��ȡ����: {ex.Message}>";
                }
            }

            public static Type GetMemberType(MemberInfo member) =>
                member switch
                {
                    PropertyInfo prop => prop.PropertyType,
                    FieldInfo field => field.FieldType,
                    _ => null
                };
        }
        #endregion

        #region ����������
        internal static class FilterFactory
        {
            public static Func<MemberInfo, bool> CreateClassPropertiesFilter(
                bool excludeCollections = true,
                bool excludePrimitives = true,
                bool excludeSystemTypes = true) =>
                member =>
                {
                    var memberType = TypeHelper.GetMemberType(member);
                    if (memberType == null) return false;

                    if (!memberType.IsClass) return false;
                    if (memberType == typeof(string)) return false;

                    if (excludeCollections && typeof(IEnumerable).IsAssignableFrom(memberType))
                        return false;

                    if (excludePrimitives && (memberType.IsPrimitive || memberType.IsValueType))
                        return false;

                    if (excludeSystemTypes && (memberType.Namespace?.StartsWith("System") == true ||
                                               memberType.Namespace?.StartsWith("Microsoft") == true))
                        return false;

                    return true;
                };

            public static Func<MemberInfo, bool> CreateNamespaceFilter(string namespacePrefix) =>
                member =>
                {
                    var memberType = TypeHelper.GetMemberType(member);
                    return memberType?.Namespace?.StartsWith(namespacePrefix) == true;
                };

            public static Func<MemberInfo, bool> CreateTypeFilter(Type baseTypeOrInterface) =>
                member =>
                {
                    var memberType = TypeHelper.GetMemberType(member);
                    return memberType != null && baseTypeOrInterface.IsAssignableFrom(memberType);
                };

            public static Func<MemberInfo, bool> CreatePrimitiveFilter() =>
                member =>
                {
                    var memberType = TypeHelper.GetMemberType(member);
                    return memberType != null && (memberType.IsPrimitive || memberType == typeof(string) || memberType == typeof(DateTime));
                };

            public static Func<MemberInfo, bool> CreateTypeFilterByDictionary(
                Dictionary<Type, Func<string, bool>> typeFilterRules,
                bool defaultResult = false,
                bool debugLog = false) =>
                member =>
                {
                    var declaringType = member.DeclaringType;
                    var memberName = member.Name;

                    if (declaringType == null)
                        return defaultResult;

                    foreach (var rule in typeFilterRules)
                    {
                        if (TypeComparer.CompareType(declaringType, rule.Key, debugLog))
                        {
                            return rule.Value(memberName);
                        }
                    }

                    foreach (var rule in typeFilterRules)
                    {
                        if (declaringType.Name == rule.Key.Name)
                        {
                            return rule.Value(memberName);
                        }
                    }

                    return defaultResult;
                };

            public static Func<MemberInfo, bool> CreateSmartFilter(
                Dictionary<Type, Func<string, bool>> topLevelFilters,
                Dictionary<Type, Func<string, bool>> nestedFilters,
                bool debugLog) =>
                member =>
                {
                    var declaringType = member.DeclaringType;
                    if (declaringType == null) return false;

                    foreach (var rule in topLevelFilters)
                    {
                        if (TypeComparer.CompareType(declaringType, rule.Key, debugLog))
                            return rule.Value(member.Name);
                    }

                    if (nestedFilters != null)
                    {
                        foreach (var rule in nestedFilters)
                        {
                            if (TypeComparer.CompareType(declaringType, rule.Key, debugLog))
                                return rule.Value(member.Name);
                        }
                    }

                    return false;
                };
        }
        #endregion

        #region �����ʽ����
        internal static class OutputFormatter
        {
            public static string FormatResultWithSeparator(string result, string separator)
            {
                return string.IsNullOrEmpty(separator) ? result : $"{separator}---{Environment.NewLine}{result}";
            }

            public static string FormatMultipleResultsWithSeparator(List<string> results, string separator)
            {
                if (results.Count == 0)
                    return FormatResultWithSeparator(string.Empty, separator);

                var separatorLine = $"{Environment.NewLine}{new string('-', 10)}{Environment.NewLine}";
                var combinedResult = string.Join(separatorLine, results);
                return FormatResultWithSeparator(combinedResult, separator);
            }

            public static string FormatComparisonResult(string inputStr, string outputStr)
            {
                return $"{inputStr}{Environment.NewLine}{new string('-', 20)}{Environment.NewLine}{outputStr}";
            }

            public static string FormatDictionaryHeader(int count, bool hasLimit, int displayCount)
            {
                var header = $"Dictionary [{count}]";
                return hasLimit ? $"{header} (��ʾǰ {displayCount} ��)" : header;
            }

            public static string FormatEnumerableHeader(int count, bool hasLimit, int displayCount)
            {
                var header = $"[{count}��Ԫ��]";
                return hasLimit ? $"{header} (��ʾǰ {displayCount} ��)" : header;
            }

            public static string FormatOmittedItemsMessage(int totalCount, int displayedCount)
            {
                return $"... (�� {totalCount} ���ʡ�� {totalCount - displayedCount} ��)";
            }

            public static int GetDisplayCount(int totalCount, int? maxItems) =>
                maxItems.HasValue ? Math.Min(totalCount, maxItems.Value) : totalCount;
        }
        #endregion

        #region �ַ�����������
        internal static class StringBuilderHelper
        {
            private static readonly Dictionary<int, string> IndentCache = new Dictionary<int, string>();

            private static string GetIndent(int indentLevel)
            {
                if (!IndentCache.TryGetValue(indentLevel, out var indent))
                {
                    indent = new string(' ', indentLevel * 2);
                    IndentCache[indentLevel] = indent;
                }
                return indent;
            }

            public static void AppendWithIndent(StringBuilder sb, int indentLevel, string text)
            {
                sb.Append(GetIndent(indentLevel));
                sb.Append(text);
            }

            public static void AppendLineWithIndent(StringBuilder sb, int indentLevel, string text)
            {
                sb.Append(GetIndent(indentLevel));
                sb.AppendLine(text);
            }

            public static void AppendLineWithIndent(StringBuilder sb, int indentLevel, char[] characters)
            {
                AppendLineWithIndent(sb, indentLevel, new string(characters));
            }
        }
        #endregion

        #region ���ͱȽ���
        internal static class TypeComparer
        {
            public static bool CompareType(Type type1, Type type2, bool debugLog = false, string logLabel = "")
            {
                if (type1 == null || type2 == null)
                    return false;

                if (type1 == type2)
                    return true;

                if (type1.FullName == type2.FullName)
                    return true;

                if (type1.Name == type2.Name)
                    return true;

                if (type1.IsAssignableFrom(type2) || type2.IsAssignableFrom(type1))
                    return true;

                return false;
            }
        }
        #endregion


        #region ������ȱȽ���
        internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
        #endregion

    }

}

