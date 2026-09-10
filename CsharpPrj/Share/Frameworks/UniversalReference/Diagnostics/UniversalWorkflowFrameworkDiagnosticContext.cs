namespace UniversalWorkflowFramework.Diagnostics;

/// <summary>
/// UniversalWorkflowFramework 诊断插桩上下文
/// <para>只负责记录结构化插桩事件（字符串 + 数据），不关心日志如何输出/持久化。</para>
/// <para>无活跃 DiagnosticContext 作用域时不执行任何操作（由调用方决定降级策略）。</para>
/// </summary>
public static class UniversalWorkflowFrameworkDiagnosticContext
{
    /// <summary>记录诊断事件（事件名 + 数据在调用处传入，消息文案由调用处字符串插值生成）。</summary>
    public static void Record(string eventName, object? data = null)
    {
        var context = Modules.DiagnosticContext.Core.DiagnosticContext.Current;
        context?.Record(eventName, data);
    }
}
