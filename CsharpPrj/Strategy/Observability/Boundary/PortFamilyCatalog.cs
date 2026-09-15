using System.Reflection;
using Strategy.Observability.Ports;

namespace Strategy.Observability.Boundary;

/// <summary>
/// 端口族目录（E16）：把程序集内「族标注 + 实现标注」编译为**可机检的族视图**——
/// 族 → 键集合、键 → 族、键 → 实现登记点文案。供诊断文案、预算守卫与文档生成共用（单一事实源）。
/// <para>
/// 纪律（任一违反即抛，禁静默择一）：① 有实现标注的类必须带族标注；② 族标注必须有实现标注；
/// ③ 族名唯一；④ 键恰属一族（跨族重叠 = 路由歧义）。
/// </para>
/// </summary>
public sealed class PortFamilyCatalog
{
    /// <summary>族名占位符（登记点文案模板内替换为族名）。</summary>
    public const string FamilyToken = "<族>";

    private readonly Dictionary<string, PortFamilySpec> _byFamilyId;
    private readonly Dictionary<string, string> _familyByPortKey;

    private PortFamilyCatalog(
        string implementationPointTemplate,
        Dictionary<string, PortFamilySpec> byFamilyId,
        Dictionary<string, string> familyByPortKey)
    {
        ImplementationPointTemplate = implementationPointTemplate;
        _byFamilyId = byFamilyId;
        _familyByPortKey = familyByPortKey;
    }

    /// <summary>登记点文案模板（族名占位符见 <see cref="FamilyToken"/>）。</summary>
    public string ImplementationPointTemplate { get; }

    /// <summary>全部族（族名 Ordinal 升序）。</summary>
    public IReadOnlyList<PortFamilySpec> Families
        => _byFamilyId.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => _byFamilyId[k]).ToArray();

    /// <summary>全部族内端口键（Ordinal 升序）。</summary>
    public IReadOnlyList<string> PortKeys
        => _familyByPortKey.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <summary>从程序集集合编译族目录（<paramref name="implementationPointTemplate"/> 含 <c>&lt;族&gt;</c> 占位符）。</summary>
    public static PortFamilyCatalog From(
        IEnumerable<Assembly> assemblies, string implementationPointTemplate, string familyToken = FamilyToken)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationPointTemplate);

        var byFamilyId = new Dictionary<string, PortFamilySpec>(StringComparer.Ordinal);
        var familyByPortKey = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var type in assemblies
                     .SelectMany(assembly => assembly.GetTypes())
                     .OrderBy(t => t.FullName ?? t.Name, StringComparer.Ordinal))
        {
            var keys = AnnotatedKeys(type);
            var family = type.GetCustomAttribute<PortFamilyAttribute>();

            if (keys.Count == 0 && family is null) continue;

            if (family is null)
                throw new InvalidOperationException(
                    $"[port:family] 类型 '{type.FullName}' 含端口实现标注但缺族标注（族声明须完备，禁匿名键）");

            if (keys.Count == 0)
                throw new InvalidOperationException(
                    $"[port:family] 族 '{family.FamilyId}'（{type.FullName}）不含任何端口实现标注（空族无意义）");

            if (byFamilyId.ContainsKey(family.FamilyId))
                throw new InvalidOperationException($"[port:family] 族名 '{family.FamilyId}' 重复登记（族名必须唯一）");

            foreach (var key in keys)
            {
                if (familyByPortKey.TryGetValue(key, out var first))
                    throw new InvalidOperationException(
                        $"[port:family] 端口键 '{key}' 跨族重复（'{first}' 与 '{family.FamilyId}'）：每键恰属一族");
                familyByPortKey[key] = family.FamilyId;
            }

            var point = family.ImplementationPoint
                        ?? implementationPointTemplate.Replace(familyToken, family.FamilyId, StringComparison.Ordinal);

            byFamilyId[family.FamilyId] = new PortFamilySpec(family.FamilyId, point, keys);
        }

        return new PortFamilyCatalog(implementationPointTemplate, byFamilyId, familyByPortKey);
    }

    /// <summary>取端口键所属族（未登记 → false）。</summary>
    public bool TryGetFamily(string portKey, out string familyId)
        => _familyByPortKey.TryGetValue(portKey ?? string.Empty, out familyId!);

    /// <summary>取族声明（未登记 → false）。</summary>
    public bool TryGetFamilySpec(string familyId, out PortFamilySpec family)
        => _byFamilyId.TryGetValue(familyId ?? string.Empty, out family!);

    /// <summary>取端口键的实现登记点文案（未登记 → null，调用方回退自身文案）。</summary>
    public string? ImplementationPointOf(string portKey)
        => TryGetFamily(portKey, out var familyId) && _byFamilyId.TryGetValue(familyId, out var family)
            ? family.ImplementationPoint
            : null;

    /// <summary>类内标注的端口键（声明序稳定 = 方法名 Ordinal 升序）。</summary>
    private static IReadOnlyList<string> AnnotatedKeys(Type type)
        => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(m => (Method: m, Key: m.GetCustomAttribute<PortImplementationAttribute>()))
            .Where(item => item.Key is not null)
            .OrderBy(item => item.Method.Name, StringComparer.Ordinal)
            .Select(item => item.Key!.PortKey)
            .ToArray();
}
