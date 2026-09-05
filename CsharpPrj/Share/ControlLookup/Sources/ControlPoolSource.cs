#nullable enable

using System.Windows.Forms;
using ECS.Bridge;

namespace Share.ControlLookup;

/// <summary>
/// ECS 控件池源（ControlPoolSource）——经 <see cref="ECS.Bridge.ControlPool"/> 的「name→Entity→Control」
/// 双向索引 O(1) 查找。作为高优先级源：ControlPool 是控件唯一事实源（Entity↔Control 直接映射），
/// 命中即代表实体已注册，最可靠。
/// </summary>
public sealed class ControlPoolSource : IControlLookupSource
{
    private readonly ControlPool _controlPool;

    public ControlPoolSource(ControlPool controlPool, int priority = 100)
    {
        _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        Priority = priority;
    }

    public string Name => "control-pool";

    public int Priority { get; }

    public bool IsReady => true;

    public Control? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // 与既有 ControlRegistryAwareFinder 行为一致：按名字槽直接取（O(1)）。
        return _controlPool.GetControl(name);
    }

    public Control? FindByName(Control root, string name)
        => FindByName(name);
}

