
#nullable enable

using System.Windows.Forms;
using FrontEndDriven.Shared.Framework;

namespace FrontEndDriven.WinForms.Applier.Projection;

/// <summary>
/// 投影策略注册表（R3）：按控件类型/特征查询投影策略，未命中回退默认 PixelProjector（与现状行为一致）。
/// 新增控件类型/平台差异 = Register 新策略，不改投影核心（WinFormApplier）。注册顺序即优先级（先注册先判定）。
/// 底层收敛到通用 <see cref="OrderedStrategyRegistry{TStrategy}"/>（有序判定 + 默认回退）。
/// </summary>
public sealed class ControlProjectionStrategyRegistry
{
    private readonly OrderedStrategyRegistry<IControlProjectionStrategy> _registry;

    public ControlProjectionStrategyRegistry(IControlProjectionStrategy? defaultStrategy = null)
    {
        _registry = new OrderedStrategyRegistry<IControlProjectionStrategy>(
            defaultStrategy ?? PixelProjector.Instance);
    }

    /// <summary>默认注册表：TabPageProjector → DockProjector → PlatformConstrainedProjector → 兜底 PixelProjector。</summary>
    public static ControlProjectionStrategyRegistry CreateDefault()
    {
        var registry = new ControlProjectionStrategyRegistry();
        registry.Register(new TabPageProjector());
        registry.Register(new DockProjector());
        registry.Register(new PlatformConstrainedProjector());
        return registry;
    }

    public void Register(IControlProjectionStrategy strategy)
        => _registry.Register(strategy);

    /// <summary>解析策略：按注册顺序首个 AppliesTo 命中者；未命中返回默认 PixelProjector。</summary>
    public IControlProjectionStrategy Resolve(ControlDescriptor desc, Control control)
        => _registry.Resolve(s => s.AppliesTo(desc, control)) ?? PixelProjector.Instance;
}
