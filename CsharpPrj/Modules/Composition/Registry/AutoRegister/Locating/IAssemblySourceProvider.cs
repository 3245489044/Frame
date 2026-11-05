namespace Modules.Registry.AutoRegister;

/// <summary>
/// 程序集源 / 布局配置的"搜索基提供端口"—— 把"就近猜根"开放为可由组合根注入的单一来源。
/// 实现本接口时 Frame 不依赖 Route：Route（布局数据源）由产品组合根解析成这些 <b>纯字符串根</b> 后注入，
/// Frame 侧只消费字符串目录，方向保持 产品 → Route、Frame 不反向依赖 Route。
/// 未注入时默认空（<see cref="DefaultAssemblySourceProvider"/>），行为退化为"运行目录 + 当前目录"旧语义。
/// </summary>
public interface IAssemblySourceProvider
{
    /// <summary>
    /// 程序集装载时追加的权威目录（绝对路径），追加在 configDir/运行目录之后、默认 ALC 按名之前。
    /// 典型来源：Route 布局 catalog 的产物根 / 权威 BuildOutput 目录（如 <c>CsharpProjectRoot\\BuildOutput\\Debug</c>）。
    /// </summary>
    IReadOnlyList<string> AssemblySearchDirectories { get; }

    /// <summary>
    /// 布局配置文件定位时追加的候选根（绝对目录）。典型来源：Route 的
    /// <c>CsharpProjectRoot</c> / <c>FrameRoot</c> —— 相对段据此派生，消费方不再拼绝对布局段。
    /// </summary>
    IReadOnlyList<string> LayoutRoots { get; }
}
