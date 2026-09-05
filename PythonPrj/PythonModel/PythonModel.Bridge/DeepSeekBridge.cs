using Python.Runtime;

namespace PythonModel.Bridge;

public sealed class DeepSeekBridge : IDeepSeekService, IDisposable
{
    private static readonly object _initLock = new();
    private static bool _pythonEngineInitialized;
    private static string _pythonHome = string.Empty;
    private static string _pythonModulePath = string.Empty;

    private bool _disposed;

    public void Initialize(string? pythonHomePath = null, string? pythonModulePath = null)
    {
        if (_pythonEngineInitialized) return;

        lock (_initLock)
        {
            if (_pythonEngineInitialized) return;

            string resolvedHome = PythonEnvironment.ResolveHome(pythonHomePath);
            string resolvedModulePath = PythonEnvironment.ResolveModulePath(pythonModulePath);

            _pythonHome = resolvedHome;
            _pythonModulePath = resolvedModulePath;

            Runtime.PythonDLL = Path.Combine(_pythonHome, PythonEnvironment.FindDllName(_pythonHome));
            PythonEngine.PythonHome = _pythonHome;
            PythonEngine.PythonPath = _pythonModulePath;

            PythonEngine.Initialize();
            _pythonEngineInitialized = true;
        }
    }

    public string ChatComplete(string systemPrompt, string userInput)
    {
        ThrowIfNotInitialized();

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
        ThrowIfNotInitialized();

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
        ThrowIfNotInitialized();

        using (Py.GIL())
        {
            dynamic deepseek = Py.Import("DeepSeekInvoke");
            deepseek.set_api_key(apiKey);
        }
    }

    public void Shutdown()
    {
        if (!_pythonEngineInitialized) return;

        lock (_initLock)
        {
            if (!_pythonEngineInitialized) return;

            try
            {
                PythonEngine.Shutdown();
            }
            catch
            {
            }
            finally
            {
                _pythonEngineInitialized = false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
    }

    private static void ThrowIfNotInitialized()
    {
        if (!_pythonEngineInitialized)
        {
            throw new InvalidOperationException(
                "PythonEngine is not initialized. Call Initialize() first.");
        }
    }
}
