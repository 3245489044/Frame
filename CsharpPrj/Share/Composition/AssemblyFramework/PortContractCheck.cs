using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.AssemblyFramework;

/// <summary>
/// 端口契约校验（单实现）：声明面 <c>[Node] fn=</c> 引用的函数名必须已在引擎注册面登记。
/// <para>
/// 原先治理链与信号链各写一份「HashSet ⇒ 差集 ⇒ <c>Name:FuncName</c> ⇒ Ordinal 排序」的表达
/// （<c>GovernanceSpecLoader</c> / <c>SignalSpecLoader</c>），差异只在缺项后的失败纪律（恒抛 vs 配置开关），
/// 故失败纪律保留在各自装载器，本类只提供成员判据。
/// </para>
/// <para>零领域类型：输入仅为字符串元组，供任意声明面装载器复用。</para>
/// </summary>
public static class PortContractCheck
{
    /// <summary>求未注册端口清单（<c>Name:FuncName</c>，Ordinal 升序；空表 = 契约满足）。</summary>
    /// <param name="ports">声明面端口（名称 + 已声明的函数名）。</param>
    /// <param name="registeredNames">引擎已注册函数名集合（<c>null</c> ⇒ 视为空集）。</param>
    public static IReadOnlyList<string> Missing(
        IEnumerable<(string Name, string FuncName)> ports,
        IEnumerable<string>? registeredNames)
    {
        ArgumentNullException.ThrowIfNull(ports);

        var registered = new HashSet<string>(registeredNames ?? Array.Empty<string>(), StringComparer.Ordinal);
        return ports
            .Where(port => !registered.Contains(port.FuncName))
            .Select(port => port.Name + ":" + port.FuncName)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
    }
}
