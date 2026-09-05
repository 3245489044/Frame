
using System;
using System.Windows.Forms;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Utils;

namespace FrontEndDriven.WinForms.Adapter;

public sealed class UiThreadDispatcherAdapter : IUiThreadDispatcher
{
    public TResult DispatchIfNeeded<TResult>(object target, Func<TResult> func)
        => UiThreadHelper.DispatchIfNeeded(target, func);

    public void DispatchIfNeeded(object target, Action action)
        => UiThreadHelper.DispatchIfNeeded(target, action);

    public Form? GetTargetForm()
        => UiThreadHelper.GetTargetForm();
}