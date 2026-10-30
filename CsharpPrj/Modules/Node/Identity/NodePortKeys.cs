using System;
using Modules.KeySpace.Model;
using Modules.KeySpace.Rules;

namespace Modules.Node.Identity;

/// <summary>
/// 节点端口键（<c>{nodeKey}.{port}</c>）**唯一构造/反解入口**：模板来自
/// <see cref="HierarchyKeyFamilies.Port"/>，本类只做装配（禁调用点手拼 <c>"."</c>）。
/// </summary>
public static class NodePortKeys
{
    private static readonly KeyOps Ops = KeyOps.FromFamilies(HierarchyKeyFamilies.All);

    /// <summary>端口键（任一身份段为空 ⇒ 空串，不产半成品键）。</summary>
    public static string Format(string? nodeKey, string? port)
    {
        if (string.IsNullOrWhiteSpace(nodeKey) || string.IsNullOrWhiteSpace(port))
        {
            return string.Empty;
        }

        var key = Ops.Root(HierarchyKeyFamilies.PortId);
        key = Ops.With(key, KeyAxis.Of("nodeKey"), nodeKey!.Trim());
        key = Ops.With(key, KeyAxis.Of("port"), port!.Trim());
        return Ops.Render(key);
    }

    /// <summary>反解端口键（失败 ⇒ false，不猜）。</summary>
    public static bool TryParse(string? raw, out string nodeKey, out string port)
    {
        nodeKey = string.Empty;
        port = string.Empty;
        if (!Ops.TryParse(HierarchyKeyFamilies.PortId, KeySurface.Storage, raw, out var parsed, out _))
        {
            return false;
        }

        nodeKey = Ops.Value(parsed, KeyAxis.Of("nodeKey")) ?? string.Empty;
        port = Ops.Value(parsed, KeyAxis.Of("port")) ?? string.Empty;
        return nodeKey.Length > 0 && port.Length > 0;
    }
}
