using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MetaCommand
{
 

    #region 控件查找器接口

    public interface IControlFinder
    {
        object? FindByName(object root, string name);

        object? FindByName(string name);

        object? FindByType(object root, Type type);

        List<object> FindAll(object root, Func<object, bool> predicate);

        object? FindForm(object control);
    }

    #endregion



    #region UI 线程调度器接口

    public interface IControlInvoker
    {
        void InvokeIfRequired(object control, Action action);

        Task RunOnUiThread(object control, Func<Task> asyncAction);

        void ExecuteOnUiThread(object control, Action action);
    }

    #endregion

    #region 统一控件服务接口

    [Obsolete("Use TConfig.Controls.IControlService")]
    public interface IControlService : IControlFinder, IControlInvoker
    {

        object? GetValue(object control);

        void SetValue(object control, object? value);

        bool IsSupported(object control);


        void BindEvent(object target, string eventName, Delegate handler);

        void UnbindEvent(object target, string eventName);

        void UnbindAllEvents(object target);



        void RegisterControl(string name, object control);

        void UnregisterControl(string name);

        object? FindRegistered(string name);

        void ClearRegistry();


        bool TrySetControlValue(string controlName, object? value, object? container = null);

        object? TryGetControlValue(string controlName, object? container = null);
    }


    #endregion
}
