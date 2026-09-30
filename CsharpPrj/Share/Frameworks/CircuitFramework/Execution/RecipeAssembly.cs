using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.Execution;

/// <summary>
/// 配方装配壳：把「配方声明 + 入口绑定表 + 节点解析 + 出口回投」四件收成单点组合，消除消费方逐段手写的
/// <c>CircuitEntry.Build → CircuitAssemblyRunner.RunAsync</c> 外壳（同构样板多份并存即漂移面：改名后静默投 null）。
/// <para>通用件：零领域类型（入口取值 / 节点解析 / 出口回投均由调用方注入）；执行唯一经
/// <see cref="CircuitAssemblyRunner"/> —— 本类只做绑定装配与转发，不引入第二条执行路径。</para>
/// </summary>
/// <typeparam name="TEntry">入口对象（外部绑定来源；配方层零领域依赖）。</typeparam>
public sealed class RecipeAssembly<TEntry> where TEntry : class
{
    private readonly IReadOnlyList<EntryBinding<TEntry>> _entryBindings;

    /// <param name="spec">声明式装配配方（拓扑单一真源）。</param>
    /// <param name="entryBindings">入口端口名 → 外部取值（须与 <see cref="CircuitRecipeSpec.EntryPorts"/> 对齐，漂移即显式抛）。</param>
    public RecipeAssembly(CircuitRecipeSpec spec, IReadOnlyList<EntryBinding<TEntry>> entryBindings)
    {
        Spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _entryBindings = entryBindings ?? throw new ArgumentNullException(nameof(entryBindings));
    }

    /// <summary>配方（声明面投影输入；供诊断与单测读取）。</summary>
    public CircuitRecipeSpec Spec { get; }

    /// <summary>驱动一次装配（入口绑定 → 框架统一校验/编译/执行 → 出口回投）。</summary>
    /// <param name="entry">入口对象。</param>
    /// <param name="resolver">节点 Kind → 黑盒实现（按入口构造：步骤体经入口取上下文）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="exitBindings">出口绑定（null = 不回投，由节点体自写上下文）。</param>
    public Task<IReadOnlyDictionary<string, object?>> RunAsync(
        TEntry entry,
        Func<TEntry, CircuitNodeResolver> resolver,
        CancellationToken cancellationToken = default,
        Func<TEntry, IReadOnlyList<ExitBinding>?>? exitBindings = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(resolver);

        return CircuitAssemblyRunner.RunAsync(
            Spec,
            CircuitEntry.Build(Spec, entry, _entryBindings),
            resolver(entry).Resolve,
            cancellationToken,
            exitBindings?.Invoke(entry));
    }
}
