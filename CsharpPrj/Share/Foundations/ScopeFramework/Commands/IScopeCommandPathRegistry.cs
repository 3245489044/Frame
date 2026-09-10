namespace Share.ScopeFramework;

/// <summary>
/// 命令 → 步骤路径决议器（可注册，替代 <see cref="ScopeCommandTranslator"/> 内 switch）。
/// 对齐项目"TryRegister* 反射注册防崩溃"惯例：注册失败不抛、未命中返回默认 S0 直通。
/// 语义权威：任务中心多层作用域封装框架设计 §4.4（转移表）。
/// </summary>
public interface IScopeCommandPathRegistry
{
    /// <summary>解析一条高层命令的步骤路径；命令无作用域或未注册时返回默认直通路径。</summary>
    IReadOnlyList<IStep> Resolve(ScopeCommand command);
}
