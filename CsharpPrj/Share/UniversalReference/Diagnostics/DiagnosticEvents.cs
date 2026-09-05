namespace UniversalWorkflowFramework.Diagnostics;

/// <summary>
/// 结构化诊断事件键契约（单一事实源）。
/// <para>值即 <see cref="UniversalWorkflowFrameworkDiagnosticContext.Record"/> 的事件名，生产/消费方共享，禁止散落字面量。</para>
/// </summary>
public static class DiagnosticEvents
{
    public static class Branch
    {
        public const string Init = "branch.init";
        public const string InitSuccess = "branch.init.success";
        public const string InitError = "branch.init.error";
        public const string RegisterSuccess = "branch.register.success";
        public const string RegisterError = "branch.register.error";
        public const string GetSuccess = "branch.get.success";
        public const string GetError = "branch.get.error";
        public const string RemoveSuccess = "branch.remove.success";
        public const string RemoveError = "branch.remove.error";
        public const string ValidateSuccess = "branch.validate.success";
        public const string ValidateError = "branch.validate.error";
        public const string RetrieveSuccess = "branch.retrieve.success";
        public const string RetrieveError = "branch.retrieve.error";
        public const string DeleteSuccess = "branch.delete.success";
        public const string DeleteError = "branch.delete.error";
        public const string ExistsError = "branch.exists.error";
        public const string ExistsGraph = "branch.exists.graph";
        public const string ExistsLocal = "branch.exists.local";
        public const string ListSuccess = "branch.list.success";
        public const string ListError = "branch.list.error";
        public const string StoreSuccess = "branch.store.success";
        public const string StoreError = "branch.store.error";
        public const string GraphValidateSuccess = "branch.graphvalidate.success";
        public const string GraphValidateError = "branch.graphvalidate.error";
        public const string GraphListSuccess = "branch.graphlist.success";
        public const string GraphListError = "branch.graphlist.error";
    }

    public static class Mapper
    {
        public const string Init = "mapper.init";
        public const string InitSuccess = "mapper.init.success";
        public const string InitError = "mapper.init.error";
        public const string MapSuccess = "mapper.map.success";
        public const string MapError = "mapper.map.error";
        public const string AddSuccess = "mapper.add.success";
        public const string AddError = "mapper.add.error";
        public const string ListSuccess = "mapper.list.success";
        public const string ListError = "mapper.list.error";
        public const string RemoveSuccess = "mapper.remove.success";
        public const string RemoveError = "mapper.remove.error";
    }

    public static class Executor
    {
        public const string Init = "executor.init";
        public const string InitSuccess = "executor.init.success";
        public const string InitError = "executor.init.error";
        public const string Error = "execute.error";
        public const string ActionNotFound = "execute.action.notfound";
        public const string Before = "execute.before";
        public const string After = "execute.after";
    }

    public static class Validate
    {
        public const string Before = "validate.before";
        public const string After = "validate.after";
        public const string Error = "validate.error";
    }

    public static class Validation
    {
        public const string Start = "validation.start";
        public const string Pass = "validation.pass";
        public const string Error = "validation.error";
    }

    public static class FieldMap
    {
        public const string Error = "fieldmap.error";
    }
}
