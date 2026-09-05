
#nullable enable

using ECS.Core;
using FrontEndDriven.Protocol;
using FrontEndDriven.Shared.Framework;

namespace FrontEndDriven.ControlLookup;

/// <summary>
/// SplitterPanel 注册表：SplitContainer 实体 + 面板侧 → 物理 Panel 的解析。
/// 底层收敛到通用 <see cref="KeyedRegistry{TKey,TValue}"/>（键 = 实体ID|面板侧）。
/// </summary>
public sealed class SplitterPanelRegistry
{
    private readonly KeyedRegistry<string, PanelEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private sealed record PanelEntry(
        Entity SplitContainerEntity,
        string PanelSide,
        object Panel);

    public void Register(Entity splitContainerEntity, string panelSide, object panel)
    {
        if (!splitContainerEntity.IsValid)
            return;
        if (string.IsNullOrEmpty(panelSide))
            return;
        if (panel == null)
            return;

        var key = BuildKey(splitContainerEntity, panelSide);
        _entries.Register(key, new PanelEntry(splitContainerEntity, panelSide, panel));
    }

    public object? Resolve(Entity splitContainerEntity, string panelSide)
        => _entries.TryGet(BuildKey(splitContainerEntity, panelSide), out var entry) ? entry.Panel : null;

    public bool IsRegistered(Entity splitContainerEntity, string panelSide)
        => _entries.Contains(BuildKey(splitContainerEntity, panelSide));

    public void Unregister(Entity splitContainerEntity)
    {
        // D8 字面量统一：面板侧值走 UIProtocol.SplitterPanel1/2 常量（与 Register 调用方同源），消除裸字面量漂移。
        _entries.Remove(BuildKey(splitContainerEntity, UIProtocol.SplitterPanel1));
        _entries.Remove(BuildKey(splitContainerEntity, UIProtocol.SplitterPanel2));
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private static string BuildKey(Entity entity, string panelSide)
        => $"{entity.Id}|{panelSide}";
}