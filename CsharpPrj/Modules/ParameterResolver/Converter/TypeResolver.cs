
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ParameterResolver.Converter;

public partial class TypeResolver
{
    private static readonly Dictionary<string, Type> BuiltInAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["string"] = typeof(string),
        ["int"] = typeof(int),
        ["integer"] = typeof(int),
        ["long"] = typeof(long),
        ["short"] = typeof(short),
        ["byte"] = typeof(byte),
        ["float"] = typeof(float),
        ["double"] = typeof(double),
        ["decimal"] = typeof(decimal),
        ["bool"] = typeof(bool),
        ["boolean"] = typeof(bool),
        ["char"] = typeof(char),
        ["datetime"] = typeof(DateTime),
        ["date"] = typeof(DateTime),
        ["timespan"] = typeof(TimeSpan),
        ["guid"] = typeof(Guid),
        ["uri"] = typeof(Uri),
        ["version"] = typeof(Version),
        ["object"] = typeof(object),
        ["any"] = typeof(object),
    };

    private static readonly ConcurrentDictionary<string, Type?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<Assembly> ScannedAssemblies = new();

    [GeneratedRegex(@"^([\w\.\+]+)`(\d+)\[([\w\.\+,\s<>\[\]]+)\]$", RegexOptions.Compiled)]
    private static partial Regex GenericTypeRegex();

    [GeneratedRegex(@"^([\w\.\+]+)(\[[\s,]*\])$", RegexOptions.Compiled)]
    private static partial Regex ArrayTypeRegex();

    public static Type? Resolve(string typeName, IEnumerable<Assembly>? hintAssemblies = null)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        if (Cache.TryGetValue(typeName, out var cached))
            return cached;

        Type? result = null;

        if (BuiltInAliases.TryGetValue(typeName, out var aliasType))
            result = aliasType;

        if (result == null)
            result = ResolveArrayType(typeName);

        if (result == null)
            result = ResolveGenericType(typeName);

        if (result == null)
            result = Type.GetType(typeName, throwOnError: false, ignoreCase: true);

        if (result == null)
            result = ScanAssemblies(typeName, hintAssemblies);

        Cache[typeName] = result;
        return result;
    }

    public static void RegisterAlias(string alias, Type type)
    {
        BuiltInAliases[alias] = type;
    }

    public static void ClearCache()
    {
        Cache.Clear();
    }

    private static Type? ScanAssemblies(string typeName, IEnumerable<Assembly>? hintAssemblies = null)
    {
        if (hintAssemblies != null)
        {
            foreach (var assembly in hintAssemblies)
            {
                var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
                if (type != null)
                {
                    ScannedAssemblies.Add(assembly);
                    return type;
                }
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!ScannedAssemblies.Contains(assembly))
            {
                ScannedAssemblies.Add(assembly);
                var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
                if (type != null)
                    return type;
            }
        }

        return null;
    }

    private static Type? ResolveArrayType(string typeName)
    {
        var match = ArrayTypeRegex().Match(typeName);
        if (!match.Success)
            return null;

        var elementTypeName = match.Groups[1].Value;
        var arraySuffix = match.Groups[2].Value;

        var elementType = Resolve(elementTypeName);
        if (elementType == null)
            return null;

        return arraySuffix switch
        {
            "[]" => elementType.MakeArrayType(),
            "[,]" => elementType.MakeArrayType(2),
            _ => null
        };
    }

    private static Type? ResolveGenericType(string typeName)
    {
        var match = GenericTypeRegex().Match(typeName);
        if (!match.Success)
            return null;

        var genericTypeName = match.Groups[1].Value;
        var typeParamCount = int.Parse(match.Groups[2].Value);
        var typeParamsStr = match.Groups[3].Value;

        var typeParamNames = typeParamsStr.Split(',', StringSplitOptions.TrimEntries);
        var typeParams = new Type[typeParamNames.Length];
        for (int i = 0; i < typeParamNames.Length; i++)
        {
            var resolved = Resolve(typeParamNames[i].Trim());
            if (resolved == null)
                return null;
            typeParams[i] = resolved;
        }

        var genericType = Resolve($"{genericTypeName}`{typeParamCount}");
        if (genericType == null || !genericType.IsGenericTypeDefinition)
            return null;

        return genericType.MakeGenericType(typeParams);
    }
}
