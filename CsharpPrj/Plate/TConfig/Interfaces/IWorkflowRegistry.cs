
namespace TConfig;

public interface IWorkflowRegistry : ITrConfigRegistry
{
    /// <summary>同步调用注册函数；未注册抛 <see cref="FunctionNotFoundException"/>。返回 handler 原始返回（不解包 Task）。</summary>
    object? Call(string name, params object?[] args);

    /// <summary>
    /// 异步调用并解包注册函数。Task 解包统一由 <see cref="TaskHelper.InvokeAndUnwrapAsync(ITrConfig, object?[], CancellationToken)"/>
    /// 单一权威负责，各实现不得自行做运行时类型判定（避免 async lambda 状态机盒漏解包）。
    /// 固化语义：
    ///  · 声明返回类型 Task&lt;T&gt; → await 后返回 T（含 .Result==null 原样返回，不额外吞值）；
    ///  · 声明返回类型 Task（非泛型）→ await 后返回 null（合法无值，非异常）；
    ///  · 非 Task → 原样返回；
    ///  · handler 抛异常 → 向上传播（async 内部异常为原始异常，不静默转 null）。
    /// </summary>
    Task<object?> CallAsync(string name, params object?[] args);

    /// <summary>把 <see cref="FunctionContext"/> 并入参数后按 <see cref="CallAsync"/> 同语义调用。具体参数并入方式由实现约定。</summary>
    Task<object?> CallWithContextAsync(string name, FunctionContext context, params object?[] args);

    bool HasFunction(string name);

    IEnumerable<string> GetFunctionNames();
}
