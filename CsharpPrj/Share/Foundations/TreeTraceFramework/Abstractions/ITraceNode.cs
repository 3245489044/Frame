
namespace Share.TreeTraceFramework.Abstractions
{
    public interface ITraceNode<TSelf> where TSelf : ITraceNode<TSelf>
    {
        string NodeId { get; }

        string Name { get; }

        IReadOnlyList<string> Labels { get; }

        IReadOnlyList<object?> Inputs { get; }

        IReadOnlyList<object?> Outputs { get; }

        IReadOnlyList<TSelf> Children { get; }

        TSelf? Parent { get; }

        IReadOnlyList<string> Records { get; }

        DateTime CreatedAt { get; }

        int Depth { get; }


        TSelf AddChild(string name, object? input = null, object? output = null, IEnumerable<string>? labels = null);

        void AddInput(object? input);

        void AddOutput(object? output);

        void AddLabel(string label);

        void AddRecord(string record);

        bool Detach();

        bool RemoveChild(string nodeId);
    }
}
