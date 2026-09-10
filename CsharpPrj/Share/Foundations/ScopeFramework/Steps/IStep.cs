namespace Share.ScopeFramework;

/// <summary>
/// 步骤链原语：一个可短路、由实现类静态定义的步骤节点（步骤路径由命令类型硬编码确定，无动态注册）。
/// 权威：任务中心多层作用域封装框架设计 §4.2。
/// </summary>
public interface IStep
{
    /// <summary>步骤标识（S0=直通，S1~S7）。</summary>
    string Id { get; }

    /// <summary>前置条件判定：返回 false 则短路跳过。</summary>
    bool CanExecute(ScopeContext scope);

    /// <summary>前置条件可执行的步骤产出强类型动作（不执行，只产出描述；S5/S6 级联会产多条），由唯一序列化器统一转 JSON。</summary>
    IReadOnlyList<ScopeAction> ProduceActions(ScopeContext scope, IScopeNamingRule naming);

    /// <summary>
    /// 步骤子项级诊断（默认接口实现，缺省空）。用于表达"守卫未装配跳过的图边子项"等
    /// 主步骤仍执行、但部分子项被短路跳过的场景（设计 §4.3 诊断契约）。翻译器在执行本步骤后收集。
    /// </summary>
    IReadOnlyList<DiagnosticEntry> Diagnostics(ScopeContext scope) => Array.Empty<DiagnosticEntry>();
}
