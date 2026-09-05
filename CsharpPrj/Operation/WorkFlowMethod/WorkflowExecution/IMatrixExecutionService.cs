using ExecutionCenter.Abstractions;
using ExecutionCenter.Matrix;

namespace WorkFlowMethod.Execution.Messages;

public interface IMatrixExecutionService
{
    Task<ITaskResponse> SubmitAsync(string functionName, Dictionary<string, object>? parameters = null);
}

public class DefaultMatrixExecutionService : IMatrixExecutionService
{
    private readonly MatrixEngine _matrixEngine;

    public DefaultMatrixExecutionService(MatrixEngine matrixEngine)
    {
        _matrixEngine = matrixEngine ?? throw new ArgumentNullException(nameof(matrixEngine));
    }

    public async Task<ITaskResponse> SubmitAsync(string functionName, Dictionary<string, object>? parameters = null)
    {
        var message = new WorkflowMethodTaskMessage(functionName, parameters);
        return await _matrixEngine.SubmitAsync(message);
    }
}