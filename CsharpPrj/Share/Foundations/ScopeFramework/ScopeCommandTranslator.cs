namespace Share.ScopeFramework;

/// <summary>
/// 高层命令 → 步骤单 JSON 序列的纯函数翻译器（短路步骤链，无内部可变状态）。
/// 执行端口 <see cref="_executePort"/> 由组装层注入（Agent 侧注入 RunJsonAsync；测试侧注入桩）。
/// 命令 → 步骤路径由 <see cref="IScopeCommandPathRegistry"/> 决议（可注册，替代原 switch），
/// JSON 生成统一走 <see cref="ScopeActionSerializer"/>（Preview 与执行共用同一动作描述）。
/// 权威：任务中心多层作用域封装框架设计 §4.2。
/// </summary>
public sealed class ScopeCommandTranslator : ITemplateExpander<ScopeCommand, IStep>
{
    private readonly IScopeNamingRule _naming;
    private readonly Func<string, Task<string>> _executePort;
    private readonly IScopeCommandPathRegistry _registry;

    public ScopeCommandTranslator(IScopeNamingRule naming, Func<string, Task<string>> executePort)
        : this(naming, executePort, ScopeCommandPathRegistry.CreateDefault())
    {
    }

    /// <summary>带可注入命令路径注册表的构造（默认注册表已预注册内置命令）。</summary>
    public ScopeCommandTranslator(
        IScopeNamingRule naming,
        Func<string, Task<string>> executePort,
        IScopeCommandPathRegistry registry)
    {
        _naming = PortGuard.Required(naming, nameof(naming));
        _executePort = PortGuard.Required(executePort, nameof(executePort));
        _registry = PortGuard.Required(registry, nameof(registry));
    }

    /// <summary>展开一条高层命令为步骤序列并逐条执行，返回聚合诊断。</summary>
    public async Task<ScopeCommandResult> ExecuteAsync(ScopeCommand command, CancellationToken ct = default)
    {
        var steps = _registry.Resolve(command);
        var diagnostics = new List<DiagnosticEntry>();
        var results = new List<string>();
        var produced = new List<string>();

        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();

            if (!step.CanExecute(command.Scope)) // 前置条件短路
            {
                diagnostics.Add(new DiagnosticEntry(step, DiagnosticKind.Skip, "前置条件不满足"));
                continue;
            }

            diagnostics.AddRange(step.Diagnostics(command.Scope)); // 子项级诊断（如守卫未装配的图边子项）

            foreach (var action in step.ProduceActions(command.Scope, _naming)) // 一步可产多条动作（S5/S6 级联）
            {
                var json = ScopeActionSerializer.Serialize(action); // JSON 生成唯一化：Preview 与执行同一描述
                produced.Add(json);
                var result = await _executePort(json).ConfigureAwait(false); // 经注入端口执行（RunJsonAsync）
                results.Add(result);
                diagnostics.Add(new DiagnosticEntry(step, DiagnosticKind.Ok, Truncate(result)));
            }
        }

        return new ScopeCommandResult(command.Cmd, results, diagnostics) { ProducedJson = produced };
    }

    /// <summary>dry-run：只展开步骤序列不执行（供 UI 预览 / select 命令；兼容原 Preview 语义返回步骤 id 序列）。</summary>
    public IReadOnlyList<IStep> Preview(ScopeCommand command) => _registry.Resolve(command);

    /// <summary>实现 <see cref="ITemplateExpander{T,TStep}"/>：展开为线性步骤序列（即 Preview，dry-run 不执行）。</summary>
    public IReadOnlyList<IStep> Expand(ScopeCommand command) => _registry.Resolve(command);

    /// <summary>dry-run：返回解析出的强类型动作描述（与 <see cref="ExecuteAsync"/> 共用同一 ProduceActions 描述，保证双路径一致）。</summary>
    public IReadOnlyList<ScopeAction> PreviewActions(ScopeCommand command)
    {
        var actions = new List<ScopeAction>();
        foreach (var step in _registry.Resolve(command))
        {
            if (step.CanExecute(command.Scope))
                actions.AddRange(step.ProduceActions(command.Scope, _naming));
        }
        return actions;
    }

    /// <summary>静态兼容入口：命令 → 固定步骤路径（默认转移表）。语义权威：<see cref="ScopeCommandPathRegistry.CreateDefault"/>。</summary>
    public static IReadOnlyList<IStep> ResolvePath(ScopeCommand command)
        => ScopeCommandPathRegistry.CreateDefault().Resolve(command);

    private static string Truncate(string s, int max = 120)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
