namespace Modules.Registry.AutoRegister;

/// <summary>
/// 程序集源（assembly source）装载的搜索基声明 —— 把散落各处的
/// "configDir / 运行目录 / 当前目录 / 默认 ALC 按名" 就近猜测收敛为一份有序、可注入的配置。
/// 本类型只承载"哪些目录/策略参与查找"，不执行业务；真正的装载引擎见 <see cref="AssemblySourceLocator"/>。
/// 设计意图（收敛）：调用方（AutoRegister / 各 *Register 库）不再手写 baseDir+CWD 就近装载，
/// 而是声明本搜索基；产品组合根可经 <see cref="IAssemblySourceCatalog"/> 注入 Route 派生的权威产物根（纯字符串），
/// Frame 不反向依赖 Route。
/// </summary>
public sealed class AssemblySourceSearchOptions
{
    /// <summary>
    /// 有序的候选目录（绝对路径）：相对 <paramref name="spec"/> 会依次与每个目录拼接探测。
    /// 顺序即语义 —— 靠前的目录优先（如 配置所在目录 → 运行目录 → 产品注入的权威产物目录）。
    /// </summary>
    public IReadOnlyList<string> SearchDirectories { get; init; } = Array.Empty<string>();

    /// <summary>是否在目录探测后，尝试以程序集简单名经默认 ALC 按名装载（已加载 / deps.json 已登记的程序集）。</summary>
    public bool TryDefaultAlcByName { get; init; } = true;

    /// <summary>是否在全部目录与按名尝试后，回退到 <c>Environment.CurrentDirectory</c>（兼容旧就近语义）。</summary>
    public bool TryCurrentDirectory { get; init; } = true;
}
