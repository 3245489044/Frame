namespace Usharp.Emit;

/// <summary>
/// <c>[Node]</c> 步骤解析**单一实现**（D-8）：同文件声明优先，缺省回退全量端口表。
/// <para>消费方：<c>UsharpRoundSpecEmitter.BuildSteps</c> 与 <c>DeclaredBundleBuilder.BuildManifest</c>。</para>
/// </summary>
public static class UsharpNodeLookup
{
    /// <summary>解析步骤键对应的 <c>[Node]</c> 端口声明（找不到 ⇒ null）。</summary>
    /// <param name="stepKey">步骤键。</param>
    /// <param name="sameFileNodes">同文件端口表（可空）。</param>
    /// <param name="globalNodes">全量端口表。</param>
    public static UsharpNodeSpec? Resolve(
        string stepKey,
        IReadOnlyDictionary<string, UsharpNodeSpec>? sameFileNodes,
        IReadOnlyDictionary<string, UsharpNodeSpec> globalNodes)
    {
        ArgumentNullException.ThrowIfNull(globalNodes);

        if (sameFileNodes is not null && sameFileNodes.TryGetValue(stepKey, out var own)) return own;
        return globalNodes.TryGetValue(stepKey, out var shared) ? shared : null;
    }
}