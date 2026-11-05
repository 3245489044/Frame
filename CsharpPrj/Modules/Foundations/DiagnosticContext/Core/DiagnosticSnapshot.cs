
namespace Modules.DiagnosticContext.Core;

public class DiagnosticSnapshot
{
    public string Name { get; }

    public DateTime CreatedAt { get; }

    public IReadOnlyList<EventEntry> Events { get; }

    public IReadOnlyDictionary<string, object?> State { get; }

    public IReadOnlyList<DiagnosticSnapshot> Children { get; }

    private DiagnosticSnapshot(
        string name,
        DateTime createdAt,
        IReadOnlyList<EventEntry> events,
        IReadOnlyDictionary<string, object?> state,
        IReadOnlyList<DiagnosticSnapshot> children)
    {
        Name = name;
        CreatedAt = createdAt;
        Events = events;
        State = state;
        Children = children;
    }

    internal static DiagnosticSnapshot FromScope(ScopeNode node)
    {
        var events = node.Events.ToList().AsReadOnly();

        var state = new Dictionary<string, object?>(node.State);

        var children = node.Children
            .Select(FromScope)
            .ToList()
            .AsReadOnly();

        return new DiagnosticSnapshot(
            node.Name,
            node.CreatedAt,
            events,
            state,
            children);
    }

    public DiagnosticContext Rehydrate()
    {
        var scope = new DiagnosticScope(Name);
        var context = new DiagnosticContext(scope);

        RestoreToScope(scope.RootNode, this);

        return context;
    }

    private static void RestoreToScope(ScopeNode scope, DiagnosticSnapshot snapshot)
    {
        foreach (var kvp in snapshot.State)
        {
            scope.SetState(kvp.Key, kvp.Value);
        }

        foreach (var evt in snapshot.Events)
        {
            scope.AddEntry(evt);
        }

        if (snapshot.Children.Count != scope.Children.Count)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[DiagnosticSnapshot] Child scope count mismatch: snapshot={snapshot.Children.Count}, scope={scope.Children.Count} for '{snapshot.Name}'. " +
                "Some child scope data may not be restored.");
        }

        int maxChildren = Math.Min(snapshot.Children.Count, scope.Children.Count);
        for (int i = 0; i < maxChildren; i++)
        {
            RestoreToScope(scope.Children[i], snapshot.Children[i]);
        }
    }
}
