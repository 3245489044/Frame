
using System;
using System.Windows.Forms;

namespace FrameworkAbstractions.WinFormsAbstractions;

public interface IUiThreadDispatcher
{
    TResult DispatchIfNeeded<TResult>(object target, Func<TResult> func);

    void DispatchIfNeeded(object target, Action action);

    Form? GetTargetForm();
}