
#nullable enable

using GraphFoundation.Abstractions;

namespace Modules.GraphProcessing.Operations
{
    public interface IDirectiveApplier<TDirective, TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        void Apply(TDirective directive, IGraph<TKey, TNodeData, TEdgeData> graph);

        string DirectiveType { get; }
    }

    public class DirectiveApplierRegistry<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly Dictionary<string, object> _appliers = new();

        public void Register<TDirective>(IDirectiveApplier<TDirective, TKey, TNodeData, TEdgeData> applier)
        {
            if (applier == null) throw new ArgumentNullException(nameof(applier));
            _appliers[applier.DirectiveType] = applier;
        }

        public IDirectiveApplier<TDirective, TKey, TNodeData, TEdgeData>? Get<TDirective>()
        {
            var typeName = typeof(TDirective).Name;
            if (_appliers.TryGetValue(typeName, out var applier))
                return applier as IDirectiveApplier<TDirective, TKey, TNodeData, TEdgeData>;
            return null;
        }

        public bool Apply<TDirective>(TDirective directive, IGraph<TKey, TNodeData, TEdgeData> graph)
        {
            var applier = Get<TDirective>();
            if (applier == null)
                return false;

            applier.Apply(directive, graph);
            return true;
        }

        public bool IsRegistered<TDirective>()
        {
            return _appliers.ContainsKey(typeof(TDirective).Name);
        }

        public IReadOnlyList<string> GetRegisteredTypes() =>
            _appliers.Keys.ToList().AsReadOnly();
    }
}
