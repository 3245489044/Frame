using System;

namespace Share.StateManagement
{
    /// <summary>
    /// 执行状态存储：在 <see cref="Modules.StateMachine.StateMachineStore{TState}"/> 之上补声明
    /// <see cref="IExecutionStateStore{TState}"/>（二者契约等价，后者为仅标记/别名接口，不新增成员）。
    /// 避免在构造默认值时对未实现标记接口的类型强转（原 InvalidCastException 根因）。
    /// </summary>
    public class ExecutionStateStore<TState> : Modules.StateMachine.StateMachineStore<TState>, IExecutionStateStore<TState>
        where TState : struct, Enum
    {
    }
}