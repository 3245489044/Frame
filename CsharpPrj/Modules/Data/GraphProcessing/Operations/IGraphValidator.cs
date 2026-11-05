
#nullable enable

using GraphFoundation.Abstractions;
using Modules.GraphProcessing.Snapshots;

namespace Modules.GraphProcessing.Operations
{
    public class ValidationResult
    {
        public bool IsValid { get; }

        public string? ErrorMessage { get; }

        public IReadOnlyList<string> Warnings { get; }

        private ValidationResult(bool isValid, string? errorMessage, IReadOnlyList<string>? warnings = null)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
            Warnings = warnings ?? Array.Empty<string>();
        }

        public static ValidationResult Success() => new(true, null);

        public static ValidationResult Failure(string error) => new(false, error);

        public static ValidationResult SuccessWithWarnings(IReadOnlyList<string> warnings)
            => new(true, null, warnings);
    }

    public interface IGraphValidator<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
        where TEdgeData : class
    {
        ValidationResult Validate(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph);

        ValidationResult ValidateSnapshot(GraphSnapshot<TNodeData, TEdgeData> snapshot);
    }

    public class DefaultGraphValidator<TKey, TNodeData, TEdgeData> : IGraphValidator<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
        where TEdgeData : class
    {
        public ValidationResult Validate(IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
        {
            if (graph == null)
                return ValidationResult.Failure("图为 null");

            var validator = GraphFoundation.Core.GraphFrame.CreateValidator(graph);
            var result = validator.Validate();

            return result.IsValid
                ? ValidationResult.Success()
                : ValidationResult.Failure(string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        public ValidationResult ValidateSnapshot(GraphSnapshot<TNodeData, TEdgeData> snapshot)
        {
            if (snapshot == null)
                return ValidationResult.Failure("快照为 null");

            snapshot.Validate(out var errors);
            return errors.Count == 0
                ? ValidationResult.Success()
                : ValidationResult.Failure(string.Join("; ", errors));
        }
    }
}
