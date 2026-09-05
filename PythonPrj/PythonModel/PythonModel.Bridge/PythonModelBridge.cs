using Python.Runtime;

namespace PythonModel.Bridge;

public sealed class PythonModelBridge : IPythonModelService, IDisposable
{
    private bool _disposed;

    public void Initialize(string? pythonHomePath = null, string? pythonModulePath = null)
    {
        PythonRuntime.Initialize(pythonHomePath, pythonModulePath);
    }

    public string ChatComplete(string systemPrompt, string userInput)
    {
        PythonRuntime.ThrowIfNotInitialized();

        using (Py.GIL())
        {
            try
            {
                dynamic deepseek = Py.Import("DeepSeekInvoke");
                return (string)deepseek.chat_complete(systemPrompt, userInput);
            }
            catch (Exception ex) when (ex is not PythonException)
            {
                throw new InvalidOperationException(
                    $"Failed to call DeepSeek chat_complete: {ex.Message}", ex);
            }
        }
    }

    public Task<string> ChatCompleteAsync(
        string systemPrompt,
        string userInput,
        CancellationToken cancellationToken = default)
    {
        PythonRuntime.ThrowIfNotInitialized();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (Py.GIL())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    dynamic deepseek = Py.Import("DeepSeekInvoke");
                    return (string)deepseek.chat_complete(systemPrompt, userInput);
                }
                catch (Exception ex) when (ex is not PythonException)
                {
                    throw new InvalidOperationException(
                        $"Failed to call DeepSeek chat_complete: {ex.Message}", ex);
                }
            }
        }, cancellationToken);
    }

    public void SetApiKey(string apiKey)
    {
        PythonRuntime.ThrowIfNotInitialized();

        using (Py.GIL())
        {
            dynamic deepseek = Py.Import("DeepSeekInvoke");
            deepseek.set_api_key(apiKey);
        }
    }

    public void Shutdown()
    {
        PythonRuntime.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
    }
}
