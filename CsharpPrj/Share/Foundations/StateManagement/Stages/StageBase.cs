
using Modules.Pipeline;

namespace Share.StateManagement.Stages
{
    public abstract class StageBase : PipelineStageBase<IStageContext>
    {
        protected StageBase(string name, string? description = null)
            : base(name, description)
        {
        }


        protected static T? GetData<T>(IStageContext context, string key)
            => context.Data.Get<T>(key);

        protected static void SetData<T>(IStageContext context, string key, T value)
            => context.Data.Set(key, value);


        protected void LogInfo(IStageContext context, string message)
            => context.LogInfo(Name, message);

        protected void LogWarn(IStageContext context, string message)
            => context.LogWarn(Name, message);

        protected void LogError(IStageContext context, string message, Exception? ex = null)
            => context.LogError(Name, message, ex);

        protected void LogDebug(IStageContext context, string message)
            => context.LogDebug(Name, message);
    }
}
