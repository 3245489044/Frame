using Modules.KeySpace.Model;

namespace Modules.KeySpace.Rules;

/// <summary>
/// 端口契约规则（收编 NodePortRules 的「声明优先 / Required 缺失可见」口径）：
/// 只判定结构（方向/族/必填/默认值/基数），不做值语义。
/// </summary>
public static class PortContractRules
{
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
