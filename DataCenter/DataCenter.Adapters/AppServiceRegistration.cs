using Operation.StateBlock.Core;
using Share.StateManagement;

namespace DataCenter.Adapters;

/// <summary>
/// 宿主服务定位器注册（FrameworkAbstractions）—— 引擎/宿主设施只在本层出现（G2）。
/// </summary>
public static class AppServiceRegistration
{
    /// <summary>把状态中心注册进宿主的应用服务定位器（具体实例 + 契约两个入口）。</summary>
    public static void Register(StateCenter center)
    {
        ArgumentNullException.ThrowIfNull(center);

        FrameworkAbstractions.ComponentRegistry.AppServiceProvider.Register(center);
        FrameworkAbstractions.ComponentRegistry.AppServiceProvider.Register<IStateCenter>(center);
    }
}
