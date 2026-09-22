namespace Usharp.Semantics;

/// <summary>
/// 层处理器绑定（<c>process=</c>）的**判定单源**：``[Matrix]|[Flow] process=`` 的「一层恰一个声明者」条件，
/// 由闸A（US1011）与产出面（schema 发射器「非法即不产出」）共用，禁止各自复写。
/// </summary>
public static class UsharpProcessBindings
{
    /// <summary>
    /// 同层出现 ≥2 个 <c>process=</c> 声明者（**在场即声明**，空值亦计入 ⇒ 框架侧必静默覆盖）的层集（保序去重）。
    /// <paramref name="bindings"/> 的层 Id 为空白者不参与（其自身形状问题由闸A 单报）。
    /// </summary>
    public static IReadOnlyList<string> MultiBoundLayers(IEnumerable<(string? Layer, string? ProcessId)> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var owners = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (layer, processId) in bindings)
        {
            if (processId is null || string.IsNullOrWhiteSpace(layer)) continue;
            owners[layer] = owners.TryGetValue(layer, out var n) ? n + 1 : 1;
        }

        return owners.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToList();
    }
}
