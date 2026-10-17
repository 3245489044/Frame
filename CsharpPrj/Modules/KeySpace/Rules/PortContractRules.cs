using Modules.KeySpace.Abstractions;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 端口契约规则（收编 NodePortRules 的「声明优先 / Required 缺失可见」口径）：
/// 只判定结构（方向/族/必填/默认值/基数），不做值语义。
/// </summary>
public static class PortContractRules
{
    /// <summary>
    /// 按节点声明的入/出键集合投影契约：声明面已有条目优先，未登记键回退为 Optional（族 = 端口族）。
    /// <para>找不到端口族时回退到首个已声明族；声明为空 ⇒ 空契约（调用方走旧推断路径）。</para>
    /// </summary>
    public static IReadOnlyList<PortContract> ForKeys(
        IKeySpaceDeclaration declaration,
        IEnumerable<string>? parameterKeys,
        IEnumerable<string>? resultKeys)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var contracts = new List<PortContract>();
        var seen = new HashSet<(PortDirection, string)>();

        Append(parameterKeys, PortDirection.In);
        Append(resultKeys, PortDirection.Out);
        return contracts;

        void Append(IEnumerable<string>? keys, PortDirection direction)
        {
            foreach (var raw in keys ?? Array.Empty<string>())
            {
                var name = raw?.Trim();
                if (string.IsNullOrEmpty(name) || !seen.Add((direction, name)))
                {
                    continue;
                }

                var declared = declaration.Ports.FirstOrDefault(
                    port => port.Direction == direction && string.Equals(port.Name, name, StringComparison.Ordinal));
                contracts.Add(declared ?? new PortContract(
                    name,
                    direction,
                    PortFamilyOf(declaration),
                    Required: false,
                    DefaultValue: null,
                    KeyCardinality.One));
            }
        }
    }

    private static string PortFamilyOf(IKeySpaceDeclaration declaration)
        => declaration.Families.FirstOrDefault(
               family => string.Equals(family.Template, "{port}", StringComparison.Ordinal))?.Id
           ?? declaration.Families.FirstOrDefault()?.Id
           ?? "port";

    public static IReadOnlyList<PortContract> OfDirection(
        IEnumerable<PortContract> ports, PortDirection direction)
        => (ports ?? Array.Empty<PortContract>())
            .Where(port => port.Direction == direction)
            .ToArray();

    /// <summary>Required 端口是否全部有值（Known 或声明默认值）。</summary>
    public static bool RequiredMissing(
        IEnumerable<PortContract> ports,
        Func<string, KeyValue<object?>> read)
    {
        foreach (var port in ports ?? Array.Empty<PortContract>())
        {
            if (!port.Required)
            {
                continue;
            }

            var value = read(port.Name);
            if (value.State != KeyValueState.Known && string.IsNullOrEmpty(port.DefaultValue))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>构造端口键引用（方向无关；族由契约给出）。</summary>
    public static KeyRef KeyOf(PortContract port)
    {
        ArgumentNullException.ThrowIfNull(port);
        return new KeyRef(port.FamilyId, port.Name, new KeyPath(new[] { port.Name }));
    }
}
