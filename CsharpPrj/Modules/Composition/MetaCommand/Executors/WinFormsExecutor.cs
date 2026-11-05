
using System.Text;
using System.Windows.Forms;
using MetaCommand;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Models;

namespace Modules.Common.WinForms
{
    public class WinFormsExecutor : ControlExecutorBase
    {
        private static readonly WinFormsControlService _controlService = new();

        public WinFormsExecutor()
            : base("winforms", "WinForms Executor", "1.0", _controlService)
        {
        }

        protected override Task BuildWorldModelAsync(WorldModel model, string? scope, CancellationToken ct)
        {
            var forms = string.IsNullOrEmpty(scope)
                ? _controlService.GetOpenForms()
                : _controlService.GetOpenForms()
                    .Where(f => f.Name == scope || f.Text == scope).ToList();

            foreach (var form in forms)
            {
                ct.ThrowIfCancellationRequested();

                var formEntity = new EntityDescriptor
                {
                    Name = form.Name,
                    Type = "Form",
                    Description = form.Text,
                    Properties = new Dictionary<string, object>
                    {
                        ["Text"] = form.Text,
                        ["Visible"] = form.Visible,
                        ["Width"] = form.Width,
                        ["Height"] = form.Height,
                        ["TopLevel"] = form.TopLevel,
                        ["WindowState"] = form.WindowState.ToString()
                    }
                };

                AddChildControls(form, formEntity, 0, ct);
                model.Entities.Add(formEntity);
            }

            return Task.CompletedTask;
        }

        private static void AddChildControls(Control parent, EntityDescriptor parentEntity, int depth, CancellationToken ct)
        {
            foreach (Control child in parent.Controls)
            {
                ct.ThrowIfCancellationRequested();

                var childEntity = new EntityDescriptor
                {
                    Name = child.Name,
                    Type = child.GetType().Name,
                    Description = child.Text,
                    Properties = new Dictionary<string, object>
                    {
                        ["Enabled"] = child.Enabled,
                        ["Visible"] = child.Visible,
                        ["Depth"] = depth + 1,
                        ["Location"] = $"{child.Left},{child.Top}",
                        ["Size"] = $"{child.Width}x{child.Height}"
                    },
                    SupportedActions = GetSupportedActions(child)
                };

                parentEntity.Properties[$"Child_{child.Name ?? child.GetType().Name}"] = childEntity.Name;

                if (child.HasChildren)
                    AddChildControls(child, childEntity, depth + 1, ct);
            }
        }

        private static List<string> GetSupportedActions(Control control)
        {
            var actions = new List<string> { "set", "get" };

            if (control is ButtonBase)
                actions.Add("click");

            if (control is System.Windows.Forms.ListView)
                actions.Add("doubleclick");

            if (control is CheckBox || control is RadioButton)
            {
                actions.Add("check");
                actions.Add("uncheck");
            }

            actions.Add("enable");
            actions.Add("disable");
            actions.Add("show");
            actions.Add("hide");

            return actions;
        }
    }
}
