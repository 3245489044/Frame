using Rge.Unified.Models;
using Rge.Unified.Resolution;
using RgeEntity = Rge.Unified.Models.Entity;

namespace ExecutionFramework.Reference;

public sealed class ReferenceResolverBridge
{
    private readonly Rge.Unified.Resolution.ReferenceResolver _resolver;

    public ReferenceResolverBridge()
        : this(new Rge.Unified.Resolution.ReferenceResolver())
    {
    }

    public ReferenceResolverBridge(Rge.Unified.Resolution.ReferenceResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public Rge.Unified.Resolution.ReferenceResolver Inner => _resolver;

    public ResolveResult Resolve(ExecutionGraph graph, RgeEntity current, string path)
        => _resolver.Resolve(graph, current, path);

    public ResolveResult ResolveParamRef(ExecutionGraph graph, RgeEntity current, ParamRef paramRef)
        => _resolver.ResolveParamRef(graph, current, paramRef);

    public Dictionary<string, object?> ResolveAll(ExecutionGraph graph, RgeEntity current, out List<string> errors)
        => _resolver.ResolveAll(graph, current, out errors);

    public static bool IsReferencePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ReferencePathParser.Parse(value).Kind switch
        {
            ReferencePathParser.ReferencePathKind.Environment => true,
            ReferencePathParser.ReferencePathKind.Constant => true,
            ReferencePathParser.ReferencePathKind.Parent => true,
            ReferencePathParser.ReferencePathKind.RootInput => true,
            _ => false
        };
    }
}