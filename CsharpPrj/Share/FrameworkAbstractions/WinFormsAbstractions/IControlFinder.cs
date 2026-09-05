
using System;
using System.Windows.Forms;

namespace FrameworkAbstractions.WinFormsAbstractions;

public interface IControlFinder
{
    TControl? FindByName<TControl>(string controlName) where TControl : Control;

    Control? FindAcrossForms(
        Func<Form, bool> formMatcher,
        Func<Form, Control?> controlLookup);

    Control? FindRecursive(Control parent, string name);
}