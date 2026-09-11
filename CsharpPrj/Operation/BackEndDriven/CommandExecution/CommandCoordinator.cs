using System.Diagnostics;
using ECS.Core;
using FrameworkAbstractions.ComponentRegistry;
using BackEndDriven.Diagnostics;
using BackEndDriven.CommandExecution.Context;
using Modules.Parsing.Parsing;
using CommandFramework.Utilities;
using CommandFramework.Execution;

namespace BackEndDriven.CommandExecution;


public class CommandSystemCoordinator
{
    private readonly EntityManager _entityManager;
    private readonly Dictionary<string, ICommandSystem> _systems = new(StringComparer.OrdinalIgnoreCase);
    private ParseCommandSystem? _parseSystem;
    private ICommandTelemetry? _telemetry;   // 运行期实时错误/慢-卡死报告（S1；可空=关闭，缺省静默）

    // —— 共享慢/卡死监视器（批级共享，替代逐命令 CTS/双定时器/Task.Run；空闲自停）——
    private static readonly TimeSpan WatchPoll = TimeSpan.FromMilliseconds(500);
    private readonly object _watchSync = new();
    private readonly List<ActiveCommand> _activeCommands = new();
    private Task? _watchLoop;
    private bool _watchRunning;

    public CommandSystemCoordinator(EntityManager entityManager)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
        RegisterDefaultSystems();
    }

    public ParseCommandSystem ParseSystem => _parseSystem!;

    private void RegisterDefaultSystems()
    {
        _parseSystem = new ParseCommandSystem(_entityManager);
        RegisterSystem(_parseSystem);
        RegisterSystem(new EchoSystem(_entityManager));
        RegisterSystem(new SleepSystem(_entityManager));
        RegisterSystem(new CloseSystem(_entityManager));
        RegisterSystem(new SideloadSystem(_entityManager));
        RegisterSystem(new ListSystem(_entityManager));
        RegisterSystem(new LoadSystem(_entityManager));
        RegisterSystem(new ExecSystem(_entityManager));
        RegisterSystem(new ExitSystem(_entityManager));
        RegisterSystem(new ShowSystem(_entityManager));
        RegisterSystem(new LookSystem(_entityManager));
        RegisterSystem(new DiscoverSystem(_entityManager));
        RegisterSystem(new AssertSystem(_entityManager));
        RegisterSystem(new ValidateSystem(_entityManager));
        RegisterSystem(new ValidateMountSystem(_entityManager));
        RegisterSystem(new ClickSystem(_entityManager));
        RegisterSystem(new SetSystem(_entityManager));
        RegisterSystem(new GetSystem(_entityManager));
        RegisterSystem(new AwaitValueSystem(_entityManager));
    }

    public void RegisterSystem(ICommandSystem system)
    {
        foreach (var action in system.HandledActions)
        {
            _systems[action] = system;
        }
    }

    /// <summary>挂接运行期实时错误/慢-卡死遥测（null 关闭；由装配方按驱动需要启用）。</summary>
    public void AttachTelemetry(ICommandTelemetry? telemetry)
        => _telemetry = telemetry;

    /// <summary>慢/卡死报告是否已启用（遥测存在且任一阈值 &gt; 0）。</summary>
    private static bool TelemetryMonitorEnabled(ICommandTelemetry? telemetry)
        => telemetry is not null
           && (telemetry.Options.SlowReport > TimeSpan.Zero || telemetry.Options.HangReport > TimeSpan.Zero);

    /// <summary>命令开始执行时登记进共享监视器（仅在监视启用时；无遥测/阈值清零 = 零登记开销）。</summary>
    private void RegisterActive(ActiveCommand cmd)
    {
        lock (_watchSync) _activeCommands.Add(cmd);
        EnsureWatchRunning();
    }

    /// <summary>命令结束（成功/失败/异常）时移出共享监视器。真卡死（永不返回）不会到达此处 → 停留供监视器判卡。</summary>
    private void UnregisterActive(ActiveCommand cmd)
    {
        lock (_watchSync) _activeCommands.Remove(cmd);
    }

    /// <summary>惰性启动共享监视器：单条后台轮询任务、批级启动、空闲自停，替代逐命令 CTS+双定时器+Task.Run。</summary>
    private void EnsureWatchRunning()
    {
        lock (_watchSync)
        {
            if (_watchRunning) return;
            if (!TelemetryMonitorEnabled(_telemetry)) return;
            _watchRunning = true;
            _watchLoop = Task.Run(WatchLoopAsync);
        }
    }

    // 声明内等待豁免：sleep/wait 的 target 即脚本声明的等待毫秒数，等待本身不是卡。
    // 扣除「声明时长 + 余量」后仍超阈才报慢/卡死——真卡死（远超声明仍未返回）依旧被捕获。
    private static readonly HashSet<string> DeclaredWaitActions = new(StringComparer.OrdinalIgnoreCase) { "sleep", "wait" };
    private const long DeclaredWaitMarginMs = 5_000;

    private static long DeclaredWaitGraceMs(CommandMetaComponent meta)
    {
        if (meta.Action is null || !DeclaredWaitActions.Contains(meta.Action)) return 0;
        return long.TryParse(meta.Target, out var ms) && ms > 0 ? ms + DeclaredWaitMarginMs : 0;
    }

    /// <summary>共享监视器主循环：周期扫描仍在执行的命令，越过 SlowReport/HangReport 即即时上报（报告型，不打断执行）。</summary>
    private async Task WatchLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(WatchPoll).ConfigureAwait(false);

                ActiveCommand[] snapshot;
                lock (_watchSync)
                {
                    if (_activeCommands.Count == 0) return; // 批空闲 → 监视器自停（下次命令惰性重启）
                    snapshot = _activeCommands.ToArray();
                }

                var telemetry = _telemetry;
                if (!TelemetryMonitorEnabled(telemetry)) return; // 遥测被拆除 → 收尾（命令仍跑，交由下次惰性重启）
                var opts = telemetry.Options;

                foreach (var cmd in snapshot)
                {
                    bool stillActive;
                    lock (_watchSync) stillActive = _activeCommands.Contains(cmd);
                    if (!stillActive) continue; // 快命令已结束，跳过

                    var elapsedMs = cmd.Watch.ElapsedMilliseconds;
                    // 声明内等待不计入慢/卡判定（sleep/wait 的 target 即声明时长）：扣除豁免后仍超阈才报，
                    // 既消「脚本显式长等待被误报卡死」，又保住「远超声明仍未返回=真卡死」的捕获能力。
                    var chargeableMs = elapsedMs - cmd.GraceMs;
                    if (chargeableMs <= 0) continue;

                    if (!cmd.SlowReported && opts.SlowReport > TimeSpan.Zero && chargeableMs >= opts.SlowReport.TotalMilliseconds)
                    {
                        cmd.SlowReported = true;
                        telemetry.OnCommandSlowOrHang(cmd.Meta.Index, cmd.Meta.RawCommand, cmd.Meta.Action, cmd.Meta.Target, elapsedMs, isHang: false);
                    }

                    if (!cmd.HangReported && opts.HangReport > TimeSpan.Zero && chargeableMs >= opts.HangReport.TotalMilliseconds)
                    {
                        cmd.HangReported = true;
                        telemetry.OnCommandSlowOrHang(cmd.Meta.Index, cmd.Meta.RawCommand, cmd.Meta.Action, cmd.Meta.Target, elapsedMs, isHang: true);
                    }
                }
            }
        }
        finally
        {
            // 任一退出路径（空闲/遥测拆除/异常）都复位运行标志，供下次命令惰性重启
            lock (_watchSync) _watchRunning = false;
        }
    }

    /// <summary>一条正在执行命令的监视登记（共享监视器的最小观测单元）。</summary>
    private sealed class ActiveCommand
    {
        public ActiveCommand(CommandMetaComponent meta, Stopwatch watch, long graceMs)
        {
            Meta = meta;
            Watch = watch;
            GraceMs = graceMs;
        }

        public CommandMetaComponent Meta { get; }
        public Stopwatch Watch { get; }

        /// <summary>声明内等待豁免毫秒（sleep/wait 的 target 声明时长 + 余量）；其它命令为 0。</summary>
        public long GraceMs { get; }

        public bool SlowReported;
        public bool HangReported;
    }

    public void Update()
    {
        // 聚拢点重构：手写 for 循环收敛为 StaticPipeline<CommandContext>
        // （命令执行内核唯一入口，CommandPipelineFactory 承载编排与中止语义）。
        // 每条 pending 命令一个独立 Step，halt/取消经 Step.CanExecute 在命令边界短路。
        var pendingEntities = _entityManager.GetPendingCommandEntities();

        var context = new CommandPipelineContext
        {
            Entities = pendingEntities,
            StopRequested = false
        };

        var pipeline = CommandPipelineFactory.Create(
            pendingEntities,
            (entity, ctx) => ProcessEntity(entity, ctx));
        pipeline.Execute(context);
    }

    /// <summary>
    /// 异步更新：逐条处理 pending 命令，对实现 <see cref="IAsyncCommandSystem"/> 的命令以 await 执行
    /// （不阻塞命令线程），其余命令走同步 <see cref="ICommandSystem.Execute"/>。
    /// </summary>
    public async Task UpdateAsync()
    {
        var pendingEntities = _entityManager.GetPendingCommandEntities();

        var context = new CommandPipelineContext
        {
            Entities = pendingEntities,
            StopRequested = false
        };

        foreach (var entity in pendingEntities)
        {
            if (context.StopRequested) break; // halt 语义在命令边界短路
            await ProcessEntityAsync(entity, context).ConfigureAwait(false);
        }
    }

    /// <summary>异步单命令更新（对应同步 <see cref="Update(Entity)"/>，供异步调用方使用）。</summary>
    public async Task UpdateAsync(Entity entity)
    {
        if (!_entityManager.TryGetComponent<CommandStatusComponent>(entity, out var status))
            return;

        if (status.State != CommandState.Pending)
            return;

        await ProcessEntityAsync(entity).ConfigureAwait(false);
    }

    public void Update(Entity entity)
    {
        if (!_entityManager.TryGetComponent<CommandStatusComponent>(entity, out var status))
            return;

        if (status.State != CommandState.Pending)
            return;

        // 单命令路径无后续命令可短路，无需承载 halt 语义的上下文（ProcessEntity 的 ctx 在此为写后即弃，
        // 收敛为 null 消除无效 StopRequested 写入——R:2.1）。
        ProcessEntity(entity);
    }

    private void ProcessEntity(Entity entity, CommandPipelineContext? ctx = null)
    {
        if (!_entityManager.TryGetComponent<CommandMetaComponent>(entity, out var meta))
            return;

        _entityManager.SetComponent(entity, new CommandStatusComponent
        {
            State = CommandState.Executing,
            DurationMs = 0
        });

        // 运行期实时：命令开始
        _telemetry?.OnCommandBegin(meta.Index, meta.RawCommand, meta.Action, meta.Target);

        var sw = Stopwatch.StartNew();
        bool hasSystem = _systems.TryGetValue(meta.Action, out var system);

        // 运行期慢/卡死报告：命令登记进共享监视器（批级共享轮询，空闲自停），替代逐命令 CTS/双定时器/Task.Run。
        // 快命令零监视资源；真卡死（同步体阻塞不返回）停留登记 → 由监视器并发判卡即时上报。
        var active = TelemetryMonitorEnabled(_telemetry) ? new ActiveCommand(meta, sw, DeclaredWaitGraceMs(meta)) : null;
        if (active is not null) RegisterActive(active);

        try
        {
            if (hasSystem)
            {
                system.Execute(entity, meta);
            }
            else
            {
                MarkFailed(entity, $"Unknown command: {meta.Action}", sw);
                return;
            }
        }
        catch (Exception ex)
        {
            MarkFailed(entity, $"Command '{meta.Action}' error: {ex.Message}", sw, ex);
            return;
        }
        finally
        {
            if (active is not null) UnregisterActive(active);
        }

        if (_entityManager.TryGetComponent<CommandStatusComponent>(entity, out var status)
            && status.State == CommandState.Executing)
        {
            MarkFailed(entity, $"Command '{meta.Action}' 执行后未设置最终状态", sw);
            return;
        }

        // halt 语义：exit/close 请求 → 停止后续命令（原 StopRequested 死代码修复）
        if (ctx != null
            && (_entityManager.HasComponent<ExitRequestComponent>(entity)
                || _entityManager.HasComponent<CloseRequestComponent>(entity)))
        {
            ctx.StopRequested = true;
        }

        // 成功路径无需再 Stop：MarkFailed 已 Stop 并记录时长；此处 Stop 为无效操作（sw 仅被 MarkFailed 消费）。
    }

    /// <summary>异步命令处理：实现 <see cref="IAsyncCommandSystem"/> 的命令以 await 执行，其余走同步。</summary>
    private async Task ProcessEntityAsync(Entity entity, CommandPipelineContext? ctx = null)
    {
        if (!_entityManager.TryGetComponent<CommandMetaComponent>(entity, out var meta))
            return;

        _entityManager.SetComponent(entity, new CommandStatusComponent
        {
            State = CommandState.Executing,
            DurationMs = 0
        });

        // 运行期实时：命令开始
        _telemetry?.OnCommandBegin(meta.Index, meta.RawCommand, meta.Action, meta.Target);

        var sw = Stopwatch.StartNew();
        bool hasSystem = _systems.TryGetValue(meta.Action, out var system);

        // 运行期慢/卡死报告：命令登记进共享监视器（批级共享轮询，空闲自停），替代逐命令 CTS/双定时器。
        // 快命令零监视资源；真卡死（await 永不返回）停留登记 → 由监视器并发判卡即时上报。
        var active = TelemetryMonitorEnabled(_telemetry) ? new ActiveCommand(meta, sw, DeclaredWaitGraceMs(meta)) : null;
        if (active is not null) RegisterActive(active);

        try
        {
            if (hasSystem)
            {
                if (system is IAsyncCommandSystem asyncSystem)
                    await asyncSystem.ExecuteAsync(entity, meta).ConfigureAwait(false);
                else
                    system.Execute(entity, meta);
            }
            else
            {
                MarkFailed(entity, $"Unknown command: {meta.Action}", sw);
                return;
            }
        }
        catch (Exception ex)
        {
            MarkFailed(entity, $"Command '{meta.Action}' error: {ex.Message}", sw, ex);
            return;
        }
        finally
        {
            if (active is not null) UnregisterActive(active);
        }

        if (_entityManager.TryGetComponent<CommandStatusComponent>(entity, out var status)
            && status.State == CommandState.Executing)
        {
            MarkFailed(entity, $"Command '{meta.Action}' 执行后未设置最终状态", sw);
            return;
        }

        // halt 语义：exit/close 请求 → 停止后续命令（对应同步 ProcessEntity）
        if (ctx != null
            && (_entityManager.HasComponent<ExitRequestComponent>(entity)
                || _entityManager.HasComponent<CloseRequestComponent>(entity)))
        {
            ctx.StopRequested = true;
        }
    }

    private void MarkFailed(Entity entity, string errorMessage, Stopwatch sw, Exception? exception = null)
    {
        sw.Stop();
        _entityManager.SetComponent(entity, new CommandStatusComponent
        {
            State = CommandState.Failed,
            ErrorMessage = errorMessage,
            DurationMs = sw.ElapsedMilliseconds
        });

        // 运行期实时：命令失败（即时出声，而非等整条脚本结束才汇总）
        if (_telemetry is { } tel
            && _entityManager.TryGetComponent<CommandMetaComponent>(entity, out var meta))
        {
            tel.OnCommandFailure(
                meta.Index, meta.RawCommand, meta.Action, meta.Target,
                errorMessage, sw.ElapsedMilliseconds, exception);
        }
    }
}
