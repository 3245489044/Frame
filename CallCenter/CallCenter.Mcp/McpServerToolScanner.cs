using System.Reflection;
using ModelContextProtocol.Server;

namespace CallCenter.Mcp;

/// <summary>
/// MCP 工具类型扫描器（SingleImplementation / TH-12）——把带 <see cref="McpServerToolTypeAttribute"/> 的类型上
/// 带 <see cref="McpServerToolAttribute"/> 的方法投影为 <see cref="McpServerTool"/> 实例集。
/// <para>
/// 存在的必要性（ModelContextProtocol 2.2.0 实测缺陷）：<c>WithTools(IEnumerable&lt;Type&gt;)</c> 不会把工具落进会话
/// ——initialize 响应的 capabilities 只有 logging、无 tools，tools/list·tools/call 均报 "no handler is available"
/// （stdio 与 HTTP 同源复现）。实测唯一生效路径 = 显式 <see cref="McpServerTool.Create(MethodInfo, object?, McpServerToolCreateOptions)"/>
/// 造工具，再放进承载装配（HTTP：会话 <c>ToolCollection</c>；stdio：<c>McpServerTool</c> 注册进 DI，由
/// <c>McpServerOptionsSetup</c> 装配）。故扫描逻辑收敛本类，HTTP/stdio 两个承载共用。
/// </para>
/// </summary>
public static class McpServerToolScanner
{
    /// <summary>
    /// 投影工具类型集为工具实例集（静态方法 target=null；实例方法以类型默认构造实例承载）。
    /// </summary>
    public static IReadOnlyList<McpServerTool> Create(IEnumerable<Type> toolTypes)
    {
        var tools = new List<McpServerTool>();
        foreach (var type in toolTypes.Distinct())
        {
            var target = ResolveTarget(type);
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            {
                if (m.GetCustomAttribute<McpServerToolAttribute>() is null) continue;
                if (m.IsStatic)
                    tools.Add(McpServerTool.Create(m, target: null, new McpServerToolCreateOptions()));
                else if (target is not null)
                    tools.Add(McpServerTool.Create(m, target, new McpServerToolCreateOptions()));
            }
        }
        return tools;
    }

    /// <summary>实例方法承载对象：静态/抽象类型返回 null（不承载实例方法）；否则默认构造一个实例。</summary>
    private static object? ResolveTarget(Type type)
    {
        if (type.IsAbstract) return null; // 静态类（abstract+sealed）无实例方法可承载
        if (type.GetConstructor(Type.EmptyTypes) is null) return null;
        try { return Activator.CreateInstance(type); } catch { return null; }
    }
}
