
using Common.PlaceholderFramework.Integrated.Handlers;

namespace Common.PlaceholderFramework.Integrated.Configuration
{

    public abstract class BuilderBase
    {
        public abstract string Name { get; }

        protected Func<string, object?, bool> LogFunc { get; set; } = DefaultLog;

        private static bool DefaultLog(string msgId, object? data)
        {
            return LogPlaceholderHandlerBase.Log(msgId, "BuilderBase", "builder:", data);
        }

        protected void Log(string eventName, object? data = null)
            => LogFunc(eventName, data);
    }


    public interface IScriptBuilder
    {
        string Name { get; }

        IScriptBuilder Load(string configFile);

        IScriptBuilder Show();

        IScriptBuilder Set(string controlName, string value);

        IScriptBuilder Click(string controlName);

        IScriptBuilder Get(string controlName);

        IScriptBuilder Sleep(int milliseconds);

        IScriptBuilder Close();

        IScriptBuilder Command(string command);

        string Build();
    }


    public interface IWorkflowBuilder
    {
        string WorkflowName { get; }

        IWorkflowBuilder WithWorkflowName(string name);

        IWorkflowBuilder AddStep(Action<IStepConfigBuilder> configure);

        IWorkflowBuilder AddOutputMapping(string externalKey, string internalKey);

        IWorkflowBuilder AddIdentityMappings(params string[] outputKeys);

        string BuildToJson(bool indented = true);
    }

    public interface IStepConfigBuilder
    {
        IStepConfigBuilder StepName(string name);

        IStepConfigBuilder FuncName(string name);

        IStepConfigBuilder InputKeys(params string[] keys);

        IStepConfigBuilder OutputKey(string key);

        IStepConfigBuilder AddParameter(object value);

        IStepConfigBuilder AddParameterRef(string placeholder);

        IStepConfigBuilder AutoParameters();
    }


    public interface IJsonConfigBuilder
    {
        string Name { get; }

        IJsonConfigBuilder WithForm(Action<IFormConfigBuilder> configure);

        IJsonConfigBuilder AddView(string name, string type, Action<IViewConfigBuilder> configure);

        string BuildToJson(bool indented = true);
    }

    public interface IFormConfigBuilder
    {
        IFormConfigBuilder Title(string title);
        IFormConfigBuilder Size(int width, int height);
        IFormConfigBuilder MaximizeBox(bool value);
        IFormConfigBuilder MinimizeBox(bool value);
        IFormConfigBuilder WindowState(string state);
        IFormConfigBuilder StartPosition(string position);
    }

    public interface IViewConfigBuilder
    {
        IViewConfigBuilder AddControl(Action<IControlConfigBuilder> configure);
    }

    public interface IControlConfigBuilder
    {
        IControlConfigBuilder Type(string type);
        IControlConfigBuilder Name(string name);
        IControlConfigBuilder Text(string text);
        IControlConfigBuilder Layout(double left, double top, double width, double height);
        IControlConfigBuilder LayoutBottom(double left, double bottom, double width, double height);
        IControlConfigBuilder Font(string fontName, int size, bool bold = false);
        IControlConfigBuilder BackColor(string color);
        IControlConfigBuilder ForeColor(string color);
        IControlConfigBuilder ReadOnly(bool value);
        IControlConfigBuilder ScrollBars(string value);
        IControlConfigBuilder Placeholder(string text);
        IControlConfigBuilder MaxLength(int length);
        IControlConfigBuilder Checked(bool value);
        IControlConfigBuilder Items(params string[] items);
        IControlConfigBuilder Event(string eventName, string functionName,
            string[]? inputs = null, string? output = null);
        IControlConfigBuilder AddChildControl(Action<IControlConfigBuilder> configure);
    }


    public static class ControlBuilderExtensions
    {
        public static IViewConfigBuilder AddToolbarButton(
            this IViewConfigBuilder view,
            string name, string text, string functionName,
            string[]? inputs = null, string? output = null)
        {
            return view.AddControl(c => c
                .Type("button")
                .Name(name)
                .Text(text)
                .BackColor("#2563EB")
                .ForeColor("White")
                .Font("Microsoft YaHei", 11, bold: true)
                .Layout(0, 0, 0.12, 0.05)
                .Event("click", functionName, inputs, output));
        }

        public static IViewConfigBuilder AddTimeButton(
            this IViewConfigBuilder view,
            string name, string text, string functionName,
            string output = "outputTextBox")
        {
            return view.AddControl(c => c
                .Type("button")
                .Name(name)
                .Text(text)
                .BackColor("#059669")
                .ForeColor("White")
                .Font("Microsoft YaHei", 11)
                .Event("click", functionName,
                    inputs: new[] { "click" },
                    output: output));
        }

        public static IViewConfigBuilder AddDangerButton(
            this IViewConfigBuilder view,
            string name, string text, string functionName,
            string[]? inputs = null, string? output = null)
        {
            return view.AddControl(c => c
                .Type("button")
                .Name(name)
                .Text(text)
                .BackColor("#DC2626")
                .ForeColor("White")
                .Font("Microsoft YaHei", 11, bold: true)
                .Event("click", functionName, inputs, output));
        }

        public static IViewConfigBuilder AddSecondaryButton(
            this IViewConfigBuilder view,
            string name, string text, string functionName,
            string[]? inputs = null, string? output = null)
        {
            return view.AddControl(c => c
                .Type("button")
                .Name(name)
                .Text(text)
                .BackColor("#6B7280")
                .ForeColor("White")
                .Font("Microsoft YaHei", 11)
                .Event("click", functionName, inputs, output));
        }

        public static IViewConfigBuilder AddLabeledInputWithButton(
            this IViewConfigBuilder view,
            string labelText, string inputName,
            string buttonName, string buttonText, string functionName)
        {
            return view
                .AddControl(c => c
                    .Type("label")
                    .Name(inputName + "Label")
                    .Text(labelText)
                    .Layout(0.05, 0.1, 0.2, 0.08))
                .AddControl(c => c
                    .Type("textbox")
                    .Name(inputName)
                    .Layout(0.25, 0.1, 0.4, 0.08))
                .AddControl(c => c
                    .Type("button")
                    .Name(buttonName)
                    .Text(buttonText)
                    .BackColor("#2563EB")
                    .ForeColor("White")
                    .Font("Microsoft YaHei", 11, bold: true)
                    .Layout(0.67, 0.1, 0.12, 0.08)
                    .Event("click", functionName));
        }

        public static IViewConfigBuilder AddTerminalOutput(
            this IViewConfigBuilder view,
            string name,
            (double left, double top, double width, double height) layout)
        {
            return view.AddControl(c => c
                .Type("textbox")
                .Name(name)
                .Layout(layout.left, layout.top, layout.width, layout.height)
                .BackColor("#1E1E1E")
                .ForeColor("#90EE90")
                .Font("Consolas", 10)
                .ReadOnly(true)
                .ScrollBars("Vertical")
                .MaxLength(0));
        }

        public static IViewConfigBuilder AddTitleBar(
            this IViewConfigBuilder view,
            string name, string text)
        {
            return view.AddControl(c => c
                .Type("label")
                .Name(name)
                .Text(text)
                .BackColor("#2563EB")
                .ForeColor("White")
                .Font("Microsoft YaHei", 12, bold: true)
                .Layout(0.0, 0.0, 1.0, 0.06));
        }

        public static IViewConfigBuilder AddStatusBar(
            this IViewConfigBuilder view,
            string name, string text)
        {
            return view.AddControl(c => c
                .Type("label")
                .Name(name)
                .Text(text)
                .ForeColor("#6B7280")
                .LayoutBottom(0.05, 0.02, 0.9, 0.04));
        }

        public static IViewConfigBuilder AddComboBox(
            this IViewConfigBuilder view,
            string name,
            (double left, double top, double width, double height) layout,
            params string[] items)
        {
            return view.AddControl(c => c
                .Type("combobox")
                .Name(name)
                .Layout(layout.left, layout.top, layout.width, layout.height)
                .Items(items));
        }
    }
}
