
using ErrorCollector = TConfig.ErrorCollector;
using PipelineContext = Modules.Pipeline.PipelineContext;

namespace Common.PlaceholderFramework.Core
{

    public interface IStructureHandler<TDocument>
    {
        string Name { get; }

        bool CanHandle(TDocument doc);

        void Process(TDocument doc, ErrorCollector errors, PipelineContext context);
    }

    public interface IStructureHandler
    {
        string Name { get; }

        bool CanHandle(object doc);

        void Process(object doc, ErrorCollector errors, PipelineContext context);
    }


    public abstract class BaseStructureHandler<TDocument> : IStructureHandler<TDocument>
    {
        public abstract string Name { get; }

        public abstract bool CanHandle(TDocument doc);

        public abstract void Process(TDocument doc, ErrorCollector errors, PipelineContext context);

        protected void ProcessItems<TItem>(
            IList<TItem> items,
            ErrorCollector errors,
            PipelineContext context,
            Action<TItem, ErrorCollector, PipelineContext>? processChild = null,
            string basePath = "$")
            where TItem : class
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue;
                processChild?.Invoke(item, errors, context);
            }
        }

        protected void ApplyDefaults<TTarget>(
            TTarget target,
            string templateName,
            IDictionary<string, object> defaults,
            string path = "$")
        {
            if (defaults == null) return;
        }
    }
}
