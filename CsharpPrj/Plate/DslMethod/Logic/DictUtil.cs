namespace Plate.DslMethod.Logic;

/// <summary>字典「缺失则建 list 再 Add」的公共样板（收敛 DslIndex/AxiomScanner/ContractRegistry/ModuleAxisMatrix 四处重复）。</summary>
internal static class DictUtil
{
    public static List<T> GetOrAdd<T>(this Dictionary<string, List<T>> map, string key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<T>();
            map[key] = list;
        }
        return list;
    }
}
