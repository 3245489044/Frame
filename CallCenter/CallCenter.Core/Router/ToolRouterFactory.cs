using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CallCenter.Core.Discovery;
using CallCenter.Core.Tracks;

namespace CallCenter.Core.Router;

/// <summary>
/// 「C# 函数库发现清单 → 全新 ToolRouter（含最新 CSharpTrack）」的单一构建入口（SingleImplementation / TH-12，
/// ANTI-DUPLICATED-IMPLEMENTATION）。收敛 Host CLI watch、Agent 组合根、测试各自手写「读 config → DiscoverDefinitions →
/// 建 CSharpTrack → AddTrack」的重复；热换以「构建全新 router 快照」为本，不改共享 router 实例。
/// </summary>
public static class ToolRouterFactory
{
    /// <summary>
    /// 从 function-libraries.json 路径发现并构建一个<b>全新</b> ToolRouter（含最新 CSharpTrack）。
    /// 清单缺失/文件不存在 → 返回空 router（含 0 轨，不抛，调用方按需处理）；重发现为空 → 同样返回空轨 router。
    /// </summary>
    public static ToolRouter BuildCSharpRouter(string configPath, ICSharpDelegateInvoker? invoker = null)
    {
        var router = new ToolRouter();
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return router;

        var source = CSharpFunctionLibrarySource.FromConfig(configPath);
        var defs = source.DiscoverDefinitions();
        if (defs is { Count: > 0 })
            router.AddTrack(new CSharpTrack(defs, invoker));

        return router;
    }

    /// <summary>从清单 JSON 文本发现并构建全新 ToolRouter（watch 先读字节快照再解析时，指纹与解析同源该快照，见 C5）。</summary>
    public static ToolRouter BuildCSharpRouterFromJson(string configJson, ICSharpDelegateInvoker? invoker = null)
        => BuildCSharpRouterFromJsonWithFactory(configJson, _ => invoker);

    /// <summary>
    /// 从清单 JSON 文本发现并构建全新 ToolRouter，执行后端经「每轮重发现的源 → invoker」工厂装配（P4-a）。
    /// 热换（EF 同栈 / Host --watch / Agent 常驻）须在每次重发现后以其<b>最新</b>注册表源装配 ExecutionFramework 执行后端
    /// （源 <see cref="CSharpFunctionLibrarySource.Registry"/> 在 <see cref="CSharpFunctionLibrarySource.DiscoverDefinitions"/> 后才有值），
    /// 故以工厂而非固定 invoker 注入——固定后端（如 Direct 或非每轮 EF）传 <c>_ => invoker</c> / <c>null</c> 即可。
    /// </summary>
    public static ToolRouter BuildCSharpRouterFromJsonWithFactory(
        string configJson, Func<CSharpFunctionLibrarySource, ICSharpDelegateInvoker?>? invokerFactory)
    {
        var router = new ToolRouter();
        if (string.IsNullOrWhiteSpace(configJson))
            return router;

        var source = CSharpFunctionLibrarySource.FromJson(configJson);
        var defs = source.DiscoverDefinitions();
        if (defs is { Count: > 0 })
            router.AddTrack(new CSharpTrack(defs, invokerFactory?.Invoke(source)));

        return router;
    }
}
