using CallCenter.Core.DataPlane;
using CallCenter.Core.Cache;
using CallCenter.Core.Discovery;
using CallCenter.Core.Router;
using CallCenter.Core.Tracks;
using CSG.Unified;
using CallCenter.Adapters;
using CallCenter.Mcp;
using CallCenter.Core.Tools;
using ExecutionFramework.Abstractions;
using ExecutionFramework.Engine;


// P0 验证闸：发现源工具 → CSG 工具表 + 一致性校验。
// P1 地基：控制面 Router + CSharpTrack Runtime 轨 —— --call 实现真实路由/执行（跨轨工具面雏形）。
var csg = new CsgFacade();
var catalog = new CsgToolCatalog(csg);

var config = LocateFunctionLibrariesConfig();
var csharpSource = BuildCSharpSource(config);

// 阶段 A 宿主接线：一次发现并复用 —— read_nodes 种子 + C# 函数库定义（描述符进目录、委托进 C#Track）。
catalog.RegisterRange(new SeedToolSource().Discover());

var csharpDefs = csharpSource?.DiscoverDefinitions();
if (csharpDefs is { Count: > 0 })
    catalog.RegisterRange(csharpDefs.Select(d => d.Descriptor));

var cliArgs = Environment.GetCommandLineArgs()[1..];
var mode = cliArgs.Length == 0 ? "list-tools" : cliArgs[0].TrimStart('-').ToLowerInvariant();

// 分层分工（B6 说明，非重复装配）：
//  · list-tools/smoke/face —— 走 CSG 数据面目录（catalog），描述符展示，无需控制面 router；
//  · call —— 控制面 Router（C#Track 经 BuildRouter 建，仅在需要真实路由执行时构建）；
//  · mcp-list-tools/mcp-call/face —— MCPBridge 发现统一经 MapBridgeToolsAsync；
//  · watch —— 独立的 Router+TrackHotReloader（热换轨生命周期由它独占持有，与一次性 CLI 调用不共享）。

// P3 TS/PS 接入：--mcp-list-tools / --mcp-call 经 stdio MCP 连 MCPBridge（node）冒烟验证（环境可选）。
if (mode == "mcp-list-tools")
    return await McpListToolsAsync();

if (mode == "mcp-call")
    return await McpCallToolAsync(cliArgs);

// P4-a 持久监视接线：--watch <function-libraries.json> 热重载 C# 轨（Ctrl+C 结束）。
if (mode == "watch")
    return await RunWatchAsync(config);

// P3 首类轨整合：--face 统一工具面（C# 轨 + 实时发现的 MCPBridge TS/PS/ExternalMCP 轨，一张表）。
if (mode == "face")
    return await UnifiedFaceAsync(catalog);

if (mode == "call")
    return await CallToolAsync(BuildRouter(csharpSource, csharpDefs), cliArgs);

switch (mode)
{
    case "list-tools":
        ListTools(catalog);
        break;

    case "smoke":
        Smoke(catalog, csg);
        break;

    default:
        Console.Error.WriteLine($"未知模式 '{mode}'。用法：--list-tools | --smoke | --call <toolName> [jsonArgs]");
        return 2;
}

return 0;

static CSharpFunctionLibrarySource? BuildCSharpSource(string? config)
{
    if (config == null)
    {
        Console.Error.WriteLine("[warn] 未定位到 function-libraries.json，本次仅含 read_nodes 临时种子。");
        return null;
    }

    return CSharpFunctionLibrarySource.FromConfig(config);
}

static ToolRouter BuildRouter(CSharpFunctionLibrarySource? csharpSource, IReadOnlyList<CSharpToolDefinition>? csharpDefs)
{
    var router = new ToolRouter();
    // C#Track 经 ExecutionFramework（统一超时/取消/异步解包/异常映射）执行，与 Func_Execute 内部同栈。
    // 执行后端单一装配收敛 ExecutionFrameworkInvokerFactory（TH-12，与 --watch / Agent 组合根共用，不再宿主各自实现）。
    if (csharpDefs is { Count: > 0 } && csharpSource is not null)
        router.AddTrack(new CSharpTrack(csharpDefs, ExecutionFrameworkInvokerFactory.Build(csharpSource)));
    return router;
}

static async Task<int> CallToolAsync(ToolRouter router, string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("用法：--call <toolName> [jsonArgs]");
        return 2;
    }

    var name = args[1];
    var argJson = args.Length > 2 ? args[2] : null;
    var result = await router.CallAsync(name, argJson);

    Console.WriteLine(result.Success ? (result.OutputJson ?? "null") : $"[error] {result.Error}");
    return result.Success ? 0 : 1;
}

static string? LocateFunctionLibrariesConfig()
{
    var inOutput = Path.Combine(AppContext.BaseDirectory, "function-libraries.json");
    if (File.Exists(inOutput)) return inOutput;

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "CallCenter", "src", "CallCenter.Host", "function-libraries.json");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }

    return null;
}

static void ListTools(CsgToolCatalog catalog)
{
    Console.WriteLine("=== CallCenter 统一工具表（CSG 数据面）===");
    foreach (var t in catalog.Tools)
        Console.WriteLine($"  {t.Name,-16} backend={t.Backend,-10} v{t.Version} flags={t.Flags}");
    Console.WriteLine($"合计 {catalog.Tools.Count} 个工具。");
}

static void Smoke(CsgToolCatalog catalog, CsgFacade csg)
{
    var form = catalog.ToCanonical();
    Console.WriteLine("=== CSG CanonicalForm ===");
    Console.WriteLine(form.ToJson());
    var vr = catalog.Validate();
    Console.WriteLine($"=== 校验: valid={vr.IsValid} errors={vr.Errors?.Count ?? 0} ===");
    Console.WriteLine($"协议: {CsgFacade.GetProtocolInfo()}");
}

// ─── P3 TS/PS 接入（MCPBridge 终端轨，stdio）───────────────────────────────
// 路径权威已下沉 CallCenter.Mcp.McpBridgeLocator（SingleImplementation，宿主不自写相对路径/上溯逻辑）。
static string? LocateMcpBridgeScript() => McpBridgeLocator.ResolveScriptPath();

static IMcpToolBackendClassifier BuildMcpClassifier()
{
    // B4：本地工具名单真源在桥 tools-definitions/*.json（name）——能读到则动态归类（新加 TS 工具自动识别），
    // 否则回落内置硬编码名单。
    var toolsDefsDir = McpBridgeLocator.ResolveToolsDefinitionsDirectory();
    return toolsDefsDir != null
        ? BridgeLocalToolClassifier.FromLocalToolsDirectory(toolsDefsDir)
        : new BridgeLocalToolClassifier();
}

/// <summary>
/// 桥发现统一投影（B6 收敛 + C2）：先经 bridge_origins 拿权威 origin 归类（本地 TS/PS vs 聚合远程 ExternalMCP），
/// 桥不支持时回落名字分类（BuildMcpClassifier）。mcp-list-tools / mcp-call / face 各模式复用。
/// </summary>
static async Task<ToolDescriptor[]> MapBridgeToolsAsync(McpClientConnection conn)
{
    return await McpBridgeDiscovery.DiscoverDescriptorsAsync(conn, BuildMcpClassifier()).ConfigureAwait(false);
}

static async Task<int> McpListToolsAsync()
{
    var script = LocateMcpBridgeScript();
    if (script == null)
    {
        Console.Error.WriteLine("[warn] 未定位到 MCPBridge build/index.js，跳过 mcp-list-tools。");
        return 0;
    }

    await using var conn = await McpClientConnection.ConnectAsync(script);
    var tools = await MapBridgeToolsAsync(conn); // B6：桥发现统一投影（含分类器/映射），不复刻编排

    Console.WriteLine("=== MCPBridge 统一工具表（经 stdio MCP 发现）===");
    foreach (var d in tools)
        Console.WriteLine($"  {d.Name,-24} backend={d.Backend,-12} flags={d.Flags}");
    Console.WriteLine($"合计 {tools.Length} 个工具。");
    return 0;
}

static async Task<int> McpCallToolAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("用法：--mcp-call <toolName> [jsonArgs]");
        return 2;
    }

    var script = LocateMcpBridgeScript();
    if (script == null)
    {
        Console.Error.WriteLine("[warn] 未定位到 MCPBridge build/index.js。");
        return 1;
    }

    var name = args[1];
    var argJson = args.Length > 2 ? args[2] : null;

    await using var conn = await McpClientConnection.ConnectAsync(script);
    var tools = await MapBridgeToolsAsync(conn); // B6：桥发现统一投影
    var track = new McpBridgeTrack(conn, tools);

    if (!track.CanCall(name))
    {
        Console.Error.WriteLine($"[error] MCPBridge 无工具 '{name}'。");
        return 1;
    }

    var result = await track.CallAsync(name, argJson);
    Console.WriteLine(result.Success ? (result.OutputJson ?? "null") : $"[error] {result.Error}");
    return result.Success ? 0 : 1;
}

// ─── P4-a 持久监视接线（FileSystemWatcher → 重发现 → 热换轨）────────────────
// 热换监视收敛共享 RouterHotReloadService（CallCenter.Core.Router，SingleImplementation/TH-12）；
// Host --watch 与 Agent 组合根复用同一实现，不再各自实现 FileSystemWatcher/防抖/内容指纹/替换。
static async Task<int> RunWatchAsync(string? initialConfigPath)
{
    var config = initialConfigPath ?? LocateFunctionLibrariesConfig();
    if (config == null)
    {
        Console.Error.WriteLine("[warn] 未定位 function-libraries.json；--watch 需 C# 库清单才能热重载。");
        return 0;
    }

    // 初始引用空 router；Start(loadInitial) 装载当前清单后即可热换。执行后端经共享
    // ExecutionFrameworkInvokerFactory（每轮重发现源 → EF 同栈），与 BuildRouter 同栈。
    using var hotReload = new RouterHotReloadService(
        new ToolRouterReference(new ToolRouter()),
        config,
        ExecutionFrameworkInvokerFactory.Build);
    hotReload.Changed += (_, e) =>
    {
        var fp = e.ContentFingerprint.Length > 8 ? e.ContentFingerprint[..8] : e.ContentFingerprint;
        Console.WriteLine($"[watch] 热重载 C#Track: {e.ToolCount} 个工具 @fp={fp}");
    };

    var applied = hotReload.ReloadNow(); // 初始装载（同单快照语义）
    hotReload.Start(loadInitial: false); // 仅开始监视，不重复装载
    Console.WriteLine(applied is not null
        ? $"[watch] 初始装载完成（指纹 {(applied.Length > 8 ? applied[..8] : applied)}）。"
        : "[watch] 初始清单为空或未变更。");

    Console.WriteLine($"[watch] 监视 {config}（Ctrl+C 结束，改文件即热重载 C# 轨）。");
    await Task.Delay(Timeout.Infinite).ConfigureAwait(false); // 常驻
    return 0;
}

// ─── P3 首类轨整合：统一工具面（C# + 实时 MCPBridge TS/PS/ExternalMCP）────────
static async Task<int> UnifiedFaceAsync(CsgToolCatalog catalog)
{
    Console.WriteLine("=== CallCenter 统一工具面（多轨: C# / TS / PS / ExternalMCP）===");
    foreach (var t in catalog.Tools)
        Console.WriteLine($"  {t.Name,-28} backend={t.Backend,-12} v{t.Version} flags={t.Flags}");

    var script = LocateMcpBridgeScript();
    if (script == null)
    {
        Console.WriteLine("  (MCPBridge 未就绪 — TS/PS/ExternalMCP 段跳过)");
        return 0;
    }

    try
    {
        await using var conn = await McpClientConnection.ConnectAsync(script);
        var bridgeTools = await MapBridgeToolsAsync(conn); // B6：桥发现统一投影
        foreach (var t in bridgeTools)
            Console.WriteLine($"  {t.Name,-28} backend={t.Backend,-12} v{t.Version} flags={t.Flags}");
        Console.WriteLine($"  桥合计 {bridgeTools.Length} 个（MCPBridge）。");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[face] 连接 MCPBridge 失败: {ex.Message}");
    }

    Console.WriteLine($"合计 {catalog.Tools.Count} 个 C#/种子工具（上方）+ MCPBridge 段。");
    return 0;
}
