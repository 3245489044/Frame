#nullable enable

using System.Windows.Forms;
using TConfig.Controls;

namespace Share.ControlLookup;

/// <summary>
/// 普通控件索引源（ControlIndexSource）——经 <see cref="IControlRegistry"/>「控件名 → Control」索引查找。
/// 对应既有多级查找链中的「Registry 兜底层」能力，作为 <see cref="ControlPoolSource"/> 之后、
/// 控件树深搜之前的中间优先级源。
/// </summary>
public sealed class ControlIndexSource : IControlLookupSource
{
    private readonly IControlRegistry _registry;

    public ControlIndexSource(IControlRegistry registry, int priority = 1_000)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Priority = priority;
    }

    public string Name => "control-index";

    public int Priority { get; }

    public bool IsReady => _registry != null;

    public Control? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return _registry.Find(name) as Control;
    }

    public Control? FindByName(Control root, string name)
        => FindByName(name);
}
