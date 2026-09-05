
using BackEndDriven.Diagnostics;

namespace BackEndDriven.Abstractions;

public abstract class ValidatorBase<TResult>
    where TResult : new()
{
    private readonly List<Func<TResult>> _validators = new();

    public void RegisterValidator(Func<TResult> validator)
    {
        _validators.Add(validator ?? throw new ArgumentNullException(nameof(validator)));
    }

    public TResult ExecuteAll(Action<string>? logCallback = null)
    {
        var combined = CreateCombinedResult();

        foreach (var validator in _validators)
        {
            try
            {
                var result = validator();
                MergeResult(combined, result);
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Validator error: {ex.Message}");
                BackEndLog.Log("ValidatorBase_Error", new { type = GetType().Name, error = ex.Message });
            }
        }

        OnAfterExecute(combined, logCallback);

        return combined;
    }

    public void Clear()
    {
        _validators.Clear();
    }

    protected abstract TResult CreateCombinedResult();

    protected abstract void MergeResult(TResult combined, TResult result);

    protected virtual void OnAfterExecute(TResult combined, Action<string>? logCallback)
    {
    }
}
