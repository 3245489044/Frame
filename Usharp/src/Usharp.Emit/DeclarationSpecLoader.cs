using System;

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
}
