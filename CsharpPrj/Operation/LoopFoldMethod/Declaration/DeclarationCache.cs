using System;
using System.Collections.Generic;
using System.IO;

namespace Operation.LoopFoldMethod.Declaration;

/// <summary>
/// **声明编译装载缓存**（通用）—— 按「解析路径 + 写入时刻」缓存编译产物，声明面装配期一次成型、逐域复用，
/// 消除「逐域重复读盘 / 编译 / 发射」。
/// <para>
/// 差异参数 = 编译委托（<see cref="IDeclarationCompiler{TCompiled}"/>）；框架不含具体语言/发射器（宿主注入）。
/// </para>
/// </summary>
/// <typeparam name="TCompiled">编译产物类型（声明面 → 可复用的中间形态）。</typeparam>
public sealed class DeclarationCache<TCompiled>
    where TCompiled : class
{
    private readonly object _gate = new();
    private readonly IDeclarationCompiler<TCompiled> _compiler;
    private string? _path;
    private DateTime _writeUtc;
    private TCompiled? _compiled;

    /// <param name="compiler">编译委托（读盘 → 编译 → 产物）。</param>
    public DeclarationCache(IDeclarationCompiler<TCompiled> compiler)
        => _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));

    /// <summary>
    /// 取编译产物：命中缓存（同路径 + 同写入时刻）即复用；否则读盘编译并回填。
    /// </summary>
    /// <param name="path">已解析的声明面绝对路径。</param>
    /// <param name="forceReload">强制重编译（测试用）。</param>
    public TCompiled GetOrCompile(string path, bool forceReload = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"声明面未命中：{path}");
        }

        var writeUtc = File.GetLastWriteTimeUtc(path);
        if (!forceReload)
        {
            lock (_gate)
            {
                if (_compiled is not null
                    && string.Equals(_path, path, StringComparison.OrdinalIgnoreCase)
                    && _writeUtc == writeUtc)
                {
                    return _compiled;
                }
            }
        }

        var compiled = _compiler.Compile(path, writeUtc);
        lock (_gate)
        {
            _path = path;
            _writeUtc = writeUtc;
            _compiled = compiled;
        }

        return compiled;
    }
}

/// <summary>声明编译委托端口（宿主注入具体语言/发射器；框架零语言依赖）。</summary>
/// <typeparam name="TCompiled">编译产物类型。</typeparam>
public interface IDeclarationCompiler<TCompiled>
    where TCompiled : class
{
    /// <summary>编译声明面（读盘 + 编译 + 发射 → 产物）。</summary>
    /// <param name="path">已解析的声明面绝对路径。</param>
    /// <param name="writeUtc">文件写入时刻（缓存键组成；供诊断）。</param>
    TCompiled Compile(string path, DateTime writeUtc);
}
