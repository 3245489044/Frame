namespace CSG.Unified;

public interface IGraph
{
    IEnumerable<string> NodeIds { get; }

    IEnumerable<(string Source, string Target)> EdgePairs { get; }

    CanonicalForm ToCanonical();
}
