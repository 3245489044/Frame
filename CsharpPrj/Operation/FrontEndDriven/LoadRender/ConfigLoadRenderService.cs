#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Container;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.State.Commands;
using Share.StateManagement;
using TConfig;

namespace FrontEndDriven.LoadRender;

/// <summary>
/// 唯一「加载即渲染」权威服务（Phase B·P1 根因，实施文档 §5.1 / §8 步骤2）：
/// 脚本 load/load-config-show 从「只读文件」升级为「真正渲染进 ECS 渲染世界」。
/// 流程：① 读 + 解析配置 → ② 定渲染目标 scope = 主内容区（contentContainer 绑定 scope，即 Content）
/// → ③ ContainerCommandExecutor.LoadToScopeAsync 渲染进主内容区（替换既有 Content 内容，作为整体融入主窗口）
/// → ④ 结构化结果。
/// 注：不再为独立配置新建窗体（用户方向：作为整体融入主窗口，Content 冲突由「同 scope 重载替换」天然消解）。
/// </summary>
public sealed class ConfigLoadRenderService : IConfigLoadRenderService
{
    /// <summary>主内容 scope 名（contentContainer 绑定 scope 反查失败时的回退字面量）。</summary>
    private const string ContentScopeFallback = "Content";

    private readonly Func<string, Task<UIConfiguration?>> _configLoader;
    private readonly ContainerCommandExecutor _executor;
    private readonly HostMountService _hostMount;
    private readonly IRenderTargetRouter _router;

    public ConfigLoadRenderService(
        Func<string, Task<UIConfiguration?>> configLoader,
        ContainerCommandExecutor executor,
        HostMountService hostMount,
        IRenderTargetRouter? router = null)
    {
        _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _hostMount = hostMount ?? throw new ArgumentNullException(nameof(hostMount));
        // F3：渲染目标按配置角色路由（消灭无条件定 Content 硬编码）；未装配时回退既有主内容区语义（向后兼容）。
        _router = router ?? new RenderTargetRouter(hostMount);
    }

    /// <summary>同步命令 ↔ 异步渲染桥（实施文档 §6.2 方案①）。命令管线在非 UI 线程 → GetResult 安全。</summary>
    public LoadRenderResult LoadAndShow(string configPath, LoadRenderOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return new LoadRenderResult { Succeeded = false, Error = "配置文件路径为空" };

        var scope = Path.GetFileNameWithoutExtension(configPath);
        try
        {
            // 方案①（实施文档 §6.2）：同步命令 ↔ 异步渲染桥。
            // 命令管线在 GUI 模式可能运行于 UI 线程（Shown 事件启动）——若直接 GetAwaiter().GetResult() 阻塞 UI 线程，
            // 渲染内部经 IUIDispatcher/EventPlatform 投递的 UI 消息无法被消息泵处理 → 自锁（Phase B 实测「运行卡死」根因）。
            // 故用 BlockOnTask：UI 线程走嵌套消息泵（DoEvents），非 UI 线程直接等待。
            return BlockOnTask(LoadAndShowCoreAsync(configPath, options ?? new LoadRenderOptions()));
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("ConfigLoadRender_Exception",
                new { scope, configPath, error = ex.Message, exceptionType = ex.GetType().FullName });
            return new LoadRenderResult
            {
                Scope = scope,
                ConfigPath = configPath,
                Succeeded = false,
                Error = $"load 渲染异常: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 同步桥：UI 线程走嵌套消息泵（防自锁），非 UI 线程直接等待。
    /// 渲染内部（IUIDispatcher.BeginInvoke / EventPlatform 事件）依赖 UI 消息泵；若命令运行在 UI 线程且同步阻塞，
    /// 投递的消息永远不被处理 → 死锁。DoEvents 泵使这些消息得以处理直至渲染完成。
    /// </summary>
    private static LoadRenderResult BlockOnTask(Task<LoadRenderResult> task)
    {
        if (SynchronizationContext.Current is System.Windows.Forms.WindowsFormsSynchronizationContext)
        {
            // 嵌套消息泵 + 安全上限（渲染为同步 ECS 管线，正常毫秒级完成；上限仅防异常永久挂起）。
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!task.IsCompleted)
            {
                System.Windows.Forms.Application.DoEvents();
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("load 渲染超时（90s），可能 UI 消息泵未驱动渲染完成。");
                Thread.Sleep(5);
            }
            return task.GetAwaiter().GetResult();
        }

        return task.GetAwaiter().GetResult();
    }

    private async Task<LoadRenderResult> LoadAndShowCoreAsync(string configPath, LoadRenderOptions options)
    {
        var configBaseName = Path.GetFileNameWithoutExtension(configPath);
        var result = new LoadRenderResult { Scope = configBaseName, ConfigPath = configPath };

        var rawText = TryReadRawText(configPath);

        // ① 配置加载（复用 StateCenter 同源 configLoader；失败/异常退化只读，保持 sideload 向后兼容）。
        UIConfiguration? config;
        try
        {
            config = await _configLoader(configPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("ConfigLoadRender_ConfigLoadFailed", new { scope = configBaseName, configPath, error = ex.Message });
            config = null;
        }

        // 非 UI 配置（无法解析为 UIConfiguration，如工作流脚本）→ 只读退化，返回原始文本（兼容旧 load/sideload 行为）。
        if (config == null)
        {
            result.Succeeded = rawText != null;
            result.ConfigText = rawText;
            result.Error = rawText == null ? "配置加载失败（无法解析为 UIConfiguration）" : null;
            return result;
        }

        // ② 定渲染目标 scope（F3：按配置角色路由——Content 进主内容区；Shell 幂等跳过/拒绝）。
        //    消灭 ResolveContentScope 无条件定 Content 的硬编码（D2）。
        var target = _router.Resolve(config, configPath);
        if (target.Action == RenderTargetAction.IdempotentSkip)
        {
            FrontEndDrivenLog.Log("ConfigLoadRender_IdempotentSkip",
                new { configBaseName, configPath, message = target.Message });
            result.Succeeded = true;
            result.Error = target.Message;
            return result;
        }

        if (target.Action == RenderTargetAction.Reject)
        {
            result.Succeeded = false;
            result.Error = target.Message;
            return result;
        }

        var contentScope = target.Scope;
        if (string.IsNullOrWhiteSpace(contentScope) || !_hostMount.IsBound(contentScope))
        {
            // 无主内容区宿主（非 GUI / 无主窗口装配）→ 只读退化，不渲染。
            result.Succeeded = rawText != null;
            result.ConfigText = rawText;
            result.Error = rawText == null ? "配置读取失败" : $"未找到主内容区宿主（{ContentScopeFallback}），仅返回配置文本";
            return result;
        }
        result.Scope = contentScope!;

        // ③ 渲染进主内容区（Content scope，替换既有内容——同 scope 重载天然消解 Content 冲突，作为整体融入主窗口）。
        FrontEndDrivenLog.Log("ConfigLoadRender_RenderIntoContent",
            new { configBaseName, contentScope, configPath });
        var ok = await _executor.LoadToScopeAsync(contentScope!, configPath).ConfigureAwait(false);
        if (!ok)
        {
            result.Succeeded = false;
            result.Error = $"scope '{contentScope}' 渲染失败（宿主为空或渲染未成功），配置可能未渲染进 ECS 渲染世界";
            return result;
        }

        result.Succeeded = true;
        result.HostChildCount = _hostMount.GetDirectChildCount(contentScope!);
        result.NodeCount = GetNodeCount(contentScope!);
        return result;
    }

    private int GetNodeCount(string scope)
    {
        try
        {
            var center = AppServiceProvider.Resolve<IStateCenter>();
            var info = center?.Scopes.GetState(scope);
            return info?.NodeCount ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string? TryReadRawText(string configPath)
    {
        try
        {
            return File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        }
        catch
        {
            return null;
        }
    }

}
