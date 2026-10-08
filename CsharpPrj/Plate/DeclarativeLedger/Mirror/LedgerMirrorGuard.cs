using System.Reflection;

namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 镜像位序对账：把**镜像模型**（装配态 / 传输态）与**权威模型**（声明态）的「公共实例属性名 + 位序」
/// 逐位比对，漂移即产出违规清单。
/// <para>
/// 动机：手写「按位构造」的映射器一旦字段序漂移即**静默错位**（无编译错、无运行错）。
/// 本守卫把该漂移变成可断言事实（守卫测试）或装配期 fail-fast 的输入。
/// </para>
/// <para>
/// 例外须**显式登记**：<paramref name="renames"/>（权威名 → 镜像名）或
/// <paramref name="allowTrailingSuffix"/>（镜像名 = 权威名 + <c>s</c>，覆盖「集合成员复数化」这类约定）。
/// 位序取 <see cref="MemberInfo.MetadataToken"/>（同一类型内的声明序近似）。
/// </para>
/// </summary>
public static class LedgerMirrorGuard
{
    /// <summary>逐位比对两模型；返回全部漂移（空 = 对齐）。</summary>
    /// <param name="authoritative">权威模型（声明态）。</param>
    /// <param name="mirror">镜像模型（装配 / 传输态）。</param>
    /// <param name="renames">显式改名表（权威名 → 镜像名）。</param>
    /// <param name="allowTrailingSuffix">允许镜像名 = 权威名 + <c>s</c>。</param>
    /// <param name="messageFactory">文案注入（域文案留消费方；缺省给通用文案）。</param>
    public static IReadOnlyList<string> Compare(
        Type authoritative,
        Type mirror,
        IReadOnlyDictionary<string, string>? renames = null,
        bool allowTrailingSuffix = false,
        Func<string, string>? messageFactory = null)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        ArgumentNullException.ThrowIfNull(mirror);

        var source = Members(authoritative);
        var target = Members(mirror);
        var violations = new List<string>();

        string Describe(string detail) => messageFactory?.Invoke(detail) ?? detail;

        for (var index = 0; index < Math.Max(source.Count, target.Count); index++)
        {
            var sourceName = index < source.Count ? source[index].Name : null;
            var mirrorName = index < target.Count ? target[index].Name : null;

            if (sourceName is null)
            {
                violations.Add(Describe($"位序 {index}：镜像 {mirror.Name} 多出成员 '{mirrorName}'"));
                continue;
            }

            if (mirrorName is null)
            {
                violations.Add(Describe($"位序 {index}：镜像 {mirror.Name} 缺少成员 '{sourceName}'"));
                continue;
            }

            var expected = renames?.GetValueOrDefault(sourceName) ?? sourceName;
            if (string.Equals(expected, mirrorName, StringComparison.Ordinal)) continue;
            if (allowTrailingSuffix && string.Equals(expected + "s", mirrorName, StringComparison.Ordinal)) continue;

            violations.Add(Describe($"位序 {index}：成员名漂移，期望 '{expected}'，实得 '{mirrorName}'"));
        }

        return violations;
    }

    /// <summary>对齐判定（无漂移）。</summary>
    public static bool IsAligned(
        Type authoritative,
        Type mirror,
        IReadOnlyDictionary<string, string>? renames = null,
        bool allowTrailingSuffix = false)
        => Compare(authoritative, mirror, renames, allowTrailingSuffix).Count == 0;

    /// <summary>公共实例属性（无索引器；按 <see cref="MemberInfo.MetadataToken"/> 近似声明序）。</summary>
    internal static List<PropertyInfo> Members(Type type)
        => [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.MetadataToken)];
}
