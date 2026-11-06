using System.Reflection;
using Modules.Node.Diagnostics;
using Modules.Node.Ports;

namespace Modules.Node.Materialization;

/// <summary>
/// 节点结果 → 输出端口的通用读取器（宿主原生值路径）。
/// <para>
/// 准入规则唯一来源 = <see cref="NodePortRules"/>（经 <see cref="NodePortBinding.ExtractValues"/>）：
/// 声明端口定向命中；未声明按成员推断并产诊断；null 结果 → 空端口；显式 null 留痕；简单值整存由调用方补默认端口。
/// 结果值优先：<see cref="IDictionary{TKey,TValue}"/> 按键直查，否则反射公共实例属性；值保持宿主原生对象（不经 JSON 往返）。
/// </para>
/// <para>特化提取器（按结果类型）由消费方保留；本类只承载默认规则，避免把领域类型写进框架。</para>
/// </summary>
public static class NodeResultReader
{
    private static readonly NodePortBinding NullSinkBinding = new();

    /// <summary>按显式结果键集合读取端口值（键不可解析时按成员名兜底并产生诊断）。</summary>
    /// <param name="resultKeys">声明结果键（可空 = 按成员推断）。</param>
    /// <param name="result">执行结果（可空 ⇒ 空端口）。</param>
    /// <param name="diagnostics">诊断汇（可空 ⇒ 不产诊断；规则与值投影不变）。</param>
    public static Dictionary<string, object?> Read(
        IEnumerable<string>? resultKeys,
        object? result,
        INodeDiagnosticsSink? diagnostics = null)
    {
        var ports = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (result is null)
        {
            return ports;
        }

        var binding = diagnostics is null ? NullSinkBinding : new NodePortBinding(diagnostics);
        var declared = resultKeys?
            .Where(static key => !string.IsNullOrEmpty(key))
            .ToArray() ?? Array.Empty<string>();
        var lookup = BuildLookup(result);

        // 成员面未知（简单值 / 无可枚举成员）→ null：落默认端口（由调用方以节点键整存），并产生 port.default 诊断。
        var members = lookup.Count == 0
            ? null
            : lookup.Select(static pair => new PortMember(pair.Key, pair.Value is not null)
            {
                Presence = pair.Value is null ? PortPresence.ExplicitNull : PortPresence.Value,
            }).ToArray();

        var outcome = binding.ExtractValues(
            nodeKey: string.Empty,
            members,
            valueOf: name => lookup[name],
            defaultValue: result,
            declaredPorts: declared,
            memberComparer: StringComparer.OrdinalIgnoreCase); // 反射/字典键沿用宽松匹配

        foreach (var pair in outcome.Ports)
        {
            if (pair.Key.Length > 0)
            {
                ports[pair.Key] = pair.Value;
            }
        }

        // 事实保真：显式 null 的声明端口必须留痕（键存在、值 null），否则可空声明永久失效。
        foreach (var pair in outcome.Presence)
        {
            if (pair.Value == PortPresence.ExplicitNull
                && declared.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                ports[pair.Key] = null;
            }
        }

        return ports;
    }

    /// <summary>结果成员面（值形态无关）：字典键优先，简单值不反射，其余反射公共实例属性。</summary>
    public static IReadOnlyDictionary<string, object?> BuildLookup(object result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var lookup = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (result is IDictionary<string, object?> dictionary)
        {
            foreach (var pair in dictionary)
            {
                lookup[pair.Key] = pair.Value;
            }

            return lookup;
        }

        if (IsSimpleValue(result))
        {
            return lookup; // 简单值 → 不反射成员，整存交由调用方默认端口
        }

        foreach (var property in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0)
            {
                lookup[property.Name] = property.GetValue(result);
            }
        }

        return lookup;
    }

    /// <summary>是否为不可再拆的简单值（不反射其成员）。</summary>
    public static bool IsSimpleValue(object value)
        => value is string or Enum or char or bool
            or byte or sbyte or short or ushort or int or uint
            or long or ulong or float or double or decimal
            or DateTime or DateTimeOffset or TimeSpan or Guid
            or DateOnly or TimeOnly;
}
