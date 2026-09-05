using System.Threading;
using System.Threading.Tasks;

namespace Share.HostAssembly.Stages;

/// <summary>
/// 装配阶段（门控单元）—— 把「注册/装配」建模为有序阶段，每阶段可声明是否可执行（CanExecute 门控）。
/// 与运行彻底分离：本接口只做一次性装配，不触碰具体业务执行。
/// </summary>
/// <typeparam name="TContext">装配上下文（承载装配配置与装配产物）。</typeparam>
public interface IAssemblyStage<TContext> where TContext : class
{
    /// <summary>阶段名（诊断/日志用，全局唯一）。</summary>
    string Name { get; }

    /// <summary>阶段职责描述。</summary>
    string Description { get; }

    /// <summary>门控判定：false 时本阶段跳过（不执行 ExecuteAsync）。</summary>
    bool CanExecute(TContext context);

    /// <summary>执行本阶段装配逻辑。</summary>
    Task ExecuteAsync(TContext context, CancellationToken ct = default);
}
