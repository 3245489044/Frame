namespace Usharp.Emit;

/// <summary>
/// 声明面装载骨架（**唯一实现**）：路径解析 → 编译 → 领域发射 → 诊断归因 → 成功判定。
/// <para>
/// 领域分歧点全部以**端口注入**：路径解析委托（配置面）与日志委托（应用层门面）由调用方给出；
/// 本层不感知任何应用层类型（单向依赖）。热路径零成本（调用方自持 Lazy 缓存）。
/// </para>
/// </summary>
public static class DeclarationSpecLoader
{
    /// <summary>按描述符解析默认路径后装载（未命中/编译失败由 Succeeded 表达，诊断经日志画像归因，不静默）。</summary>
    public static DeclarationLoadResult<T> Load<T>(DeclarationSpec<T> spec, Func<string, string?> resolvePath, Action<string, object?> log) where T : class
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(resolvePath);
        ArgumentNullException.ThrowIfNull(log);
        return LoadAt(spec, resolvePath(spec.ConfigRelativePath), log);
    }

    /// <summary>
    /// 按显式路径原样装载（null = 该级未提供，交框架按 NotDeclared 归因，不回落默认解析）——
    /// 供「默认路径装载与用例取数共用同一实现」的装载器保留其路径语义。
    /// </summary>
    public static DeclarationLoadResult<T> LoadAt<T>(DeclarationSpec<T> spec, string? resolvedPath, Action<string, object?> log) where T : class
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(log);

        var source = new DeclarationSource(spec.BaseName, resolvedPath, spec.FlowId);
        return UsharpDeclarationLoader.Load(source, spec.Emit, spec.Log.CreateSink(log));
    }

    /// <summary>取声明面产物（默认路径）；未声明/编译失败/发射失败 ⇒ null（调用方据此回退，不抛）。</summary>
    public static T? TryLoadDeclared<T>(DeclarationSpec<T> spec, Func<string, string?> resolvePath, Action<string, object?> log) where T : class
    {
        var result = Load(spec, resolvePath, log);
        return result.Succeeded ? result.Value : null;
    }

    /// <summary>取声明面产物（显式路径原样）；未声明/编译失败/发射失败 ⇒ null（调用方据此回退，不抛）。</summary>
    public static T? TryLoadDeclaredAt<T>(DeclarationSpec<T> spec, string? resolvedPath, Action<string, object?> log) where T : class
    {
        var result = LoadAt(spec, resolvedPath, log);
        return result.Succeeded ? result.Value : null;
    }

    /// <summary>
    /// 由**声明库条目**装载（**盘/库同形**入口）：条目由宿主从 <see cref="Usharp.Semantics.IDeclarationStore"/>
    /// 取出（文件库 / 数据库库），本方法不再解析路径 —— 装载骨架与路径装载共用同一尾段（<see cref="UsharpDeclarationLoader.LoadFromEntries{T}"/>）。
    /// </summary>
    /// <param name="spec">声明面描述符（<c>ConfigRelativePath</c> 仅作逻辑归因，不作物理解析）。</param>
    /// <param name="entries">已装载声明条目（空集 ⇒ 判「未声明」）。</param>
    /// <param name="log">日志委托。</param>
    public static DeclarationLoadResult<T> LoadFromEntries<T>(
        DeclarationSpec<T> spec, IReadOnlyList<Usharp.Semantics.DeclarationEntry> entries, Action<string, object?> log)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(log);

        var source = new DeclarationSource(spec.BaseName, spec.ConfigRelativePath, spec.FlowId);
        return UsharpDeclarationLoader.LoadFromEntries(entries, source, spec.Emit, spec.Log.CreateSink(log));
    }

    /// <summary>
    /// 取声明面产物（声明库条目来源）；未声明/编译失败/发射失败 ⇒ null（调用方据此回退，不抛）。
    /// </summary>
    public static T? TryLoadDeclaredFromEntries<T>(
        DeclarationSpec<T> spec, IReadOnlyList<Usharp.Semantics.DeclarationEntry> entries, Action<string, object?> log)
        where T : class
    {
        var result = LoadFromEntries(spec, entries, log);
        return result.Succeeded ? result.Value : null;
    }
}
