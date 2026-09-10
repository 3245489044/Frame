
using System.Threading;
using BasicMethod.Declarative;
using Framework.Tree;
using TConfig;

namespace Share.EventBindingOrchestrator.Parameters;

public class ResolverContext
{

    public IDictionary<string, object> Source { get; set; } = new Dictionary<string, object>();

    public string? ControlName { get; set; }

    public string? EventType { get; set; }

    public List<IDictionary<string, object>>? MergeSources { get; set; }


    public TreeIndex<ControlConfig, string> ControlIndex { get; set; } = null!;

    public ReaderWriterLockSlim IndexLock { get; set; } = null!;


    public List<ControlConfig> ControlConfigs { get; set; } = new();


    public EventBindingDeclaration<IReadOnlyList<ParameterBinding>>? Declaration { get; set; }

    public List<EventBindingDeclaration<IReadOnlyList<ParameterBinding>>> AllDeclarations { get; set; } = new();

    public IEventBindingInfo? BindingInfo { get; set; }


    public static IReadOnlyDictionary<string, string>? MapOutputMapping(
        IReadOnlyDictionary<string, string>? source)
    {
        if (source == null || source.Count == 0)
            return null;

        return new Dictionary<string, string>(source);
    }
}
