namespace PythonModel.Bridge;

public interface IPythonModelService
{
    void Initialize(string? pythonHomePath = null, string? pythonModulePath = null);

    string ChatComplete(string systemPrompt, string userInput);

    Task<string> ChatCompleteAsync(string systemPrompt, string userInput, CancellationToken cancellationToken = default);

    void SetApiKey(string apiKey);

    void Shutdown();
}
