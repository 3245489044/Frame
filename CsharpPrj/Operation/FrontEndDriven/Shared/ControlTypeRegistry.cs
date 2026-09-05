
#nullable enable

using System.Collections.Generic;
using Plate.MarkdownViewer;
using Plate.HtmlViewer;

using System.Windows.Forms;
using Common.PlaceholderFramework.SchemaValidation;

namespace FrontEndDriven.Shared;

public static class ControlTypeRegistry
{
    private sealed record ControlTypeEntry(
        string TypeName,
        Func<object> ControlFactory,
        Func<JsonSchema> SchemaFactory,
        bool IsContainer = false);

    private static readonly List<ControlTypeEntry> _entries = new();

    private static readonly Dictionary<string, JsonSchema> _schemaCache = new();

    public static IReadOnlyDictionary<string, Func<object>> ControlFactories { get; }

    public static IReadOnlyDictionary<string, Func<JsonSchema>> SchemaFactories { get; }

    public static HashSet<string> ContainerTypes { get; }

    static ControlTypeRegistry()
    {
        RegisterAll();

        ControlFactories = new Dictionary<string, Func<object>>(
            _entries.Select(e => new KeyValuePair<string, Func<object>>(e.TypeName, e.ControlFactory)),
            StringComparer.OrdinalIgnoreCase);

        SchemaFactories = new Dictionary<string, Func<JsonSchema>>(
            _entries.Select(e => new KeyValuePair<string, Func<JsonSchema>>(e.TypeName, e.SchemaFactory)),
            StringComparer.OrdinalIgnoreCase);

        // 阶段C（语义层汇聚）：容器类型词表唯一真源 = 共享语义层 ControlTypeCatalog；
        // 此处取目录中「已注册的容器类型」交集，保证与既有工厂注册一致，避免词表与注册漂移。
        var registeredTypes = new HashSet<string>(_entries.Select(e => e.TypeName), StringComparer.OrdinalIgnoreCase);
        ContainerTypes = new HashSet<string>(
            TConfig.Controls.ControlTypeCatalog.All
                .Where(c => c.IsContainer && registeredTypes.Contains(c.TypeName))
                .Select(c => c.TypeName),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void Register(string typeName, Func<object> controlFactory, Func<JsonSchema> schemaFactory, bool isContainer = false)
    {
        _entries.Add(new ControlTypeEntry(typeName, controlFactory, schemaFactory, isContainer));
    }

    private static void RegisterAll()
    {
        RegisterSimpleText("button",       () => new Button());
        RegisterSimpleText("label",        () => new Label());
        RegisterSimpleText("linklabel",    () => new LinkLabel());

        Register("checkbox", () => new CheckBox(),
            () => ContractBuilder.BaseControl().WithOptional("text", "checked").Build());

        Register("combobox", () => new ComboBox(),
            () => ContractBuilder.BaseControl().WithOptional("text", "items", "selectedIndex").Build());

        Register("listview", () => new ListView(),
            () => ContractBuilder.BaseControl()
                .WithOptional("view", "fullRowSelect", "gridLines", "columns", "items")
                .WithAllowedValues("view", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "details", "largeicon", "smallicon", "list", "tile" })
                .Build());

        RegisterRange("trackbar",       () => new TrackBar());
        RegisterRange("progressbar",    () => new ProgressBar());
        RegisterRange("numericupdown",  () => new NumericUpDown());

        Register("listbox", () => new ListBox(),
            () => ContractBuilder.BaseControl().WithOptional("text", "items", "selectedIndex", "selectionMode").Build());

        Register("checkedlistbox", () => new CheckedListBox(),
            () => ContractBuilder.BaseControl().WithOptional("text", "items", "checkedItems").Build());

        Register("domainupdown", () => new DomainUpDown(),
            () => ContractBuilder.BaseControl().WithOptional("text", "items", "selectedIndex").Build());

        Register("treeview", () => new TreeView(),
            () => ContractBuilder.BaseControl().WithOptional("text", "nodes", "showLines", "showRootLines", "showPlusMinus").Build());

        Register("datagridview", () => new DataGridView(),
            () => ContractBuilder.BaseControl().WithOptional("text", "columns", "rows", "readOnly", "allowUserToAddRows").Build());

        Register("picturebox", () => new PictureBox(),
            () => ContractBuilder.BaseControl().WithOptional("text", "image", "sizeMode", "borderStyle").Build());

        Register("richtextbox", () => new RichTextBox(),
            () => ContractBuilder.BaseControl().WithOptional("text", "readOnly").Build());

        Register("maskedtextbox", () => new MaskedTextBox(),
            () => ContractBuilder.BaseControl().WithOptional("text", "mask", "readOnly").Build());

        Register("datetimepicker", () => new DateTimePicker(),
            () => ContractBuilder.BaseControl().WithOptional("text", "value", "format", "customFormat").Build());

        Register("splitter", () => new Splitter(),
            () => ContractBuilder.BaseControl().WithOptional("text").Build());

        Register("propertygrid", () => new PropertyGrid(),
            () => ContractBuilder.BaseControl().WithOptional("text", "selectedObject", "propertySort", "toolbarVisible").Build());

        Register("webbrowser", () => new WebBrowser(),
            () => ContractBuilder.BaseControl().WithOptional("text", "url", "documentText", "allowNavigation", "allowWebBrowserDrop").Build());
        Register("markdownviewer", () => new MarkdownViewer(),
            () => ContractBuilder.BaseControl().WithOptional("markdown").Build());
        Register("htmlviewer", () => new HtmlViewer(),
            () => ContractBuilder.BaseControl().WithOptional("html", "fontSize").Build());


        Register("textbox", () => new TextBox(),
            () => ContractBuilder.BaseControl()
                .WithOptional("text", "multiline", "readOnly", "scrollBars", "maxLength", "acceptsReturn")
                .WithAllowedValues("scrollBars", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "none", "horizontal", "vertical", "both" })
                .Build());

        RegisterStrip("statusstrip",        () => new StatusStrip());
        RegisterStrip("toolstrip",          () => new ToolStrip());
        RegisterStrip("menustrip",          () => new MenuStrip());
        RegisterStrip("contextmenustrip",   () => new ContextMenuStrip());

        RegisterContainer("groupbox",           () => new GroupBox(), "text");
        RegisterContainer("flowlayoutpanel",    () => new FlowLayoutPanel());
        RegisterContainer("tablelayoutpanel",   () => new TableLayoutPanel());

        RegisterContainer("tabcontrol", () => new TabControl(), "text", "selectedIndex", "tabPages");

        RegisterContainer("tabpage", () => new TabPage());

        RegisterContainer("container", () => new Panel(),
            "text", "container.sourceType", "container.defaultConfig", "container.autoLoad");

        RegisterContainer("panel", () => new Panel(), "style");

        // 兼容存量 JSON 中 type:"form" 根容器：映射为 Panel（root 实体为容器面板而非窗体）
        RegisterContainer("form", () => new Panel(), "style", "title");

        Register("splitcontainer", () => new SplitContainer(),
            () => ContractBuilder.BaseControl()
                .WithRequired("orientation")
                .WithOptional("text", "splitterDistance", "panel1Controls", "panel2Controls")
                .WithAllowedValues("orientation", new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "vertical", "horizontal" })
                .Build(),
            isContainer: true);

        Register("splitterpanel", () => new Panel(),
            () => ContractBuilder.ContainerControl().Build(),
            isContainer: true);
    }


    private static void RegisterSimpleText(string typeName, Func<object> factory)
        => Register(typeName, factory, () => GetOrBuildSchema("text"));

    private static void RegisterRange(string typeName, Func<object> factory)
        => Register(typeName, factory, () => GetOrBuildSchema("min,max,value"));

    private static void RegisterStrip(string typeName, Func<object> factory)
        => Register(typeName, factory, () => GetOrBuildSchema("text,items"));

    private static void RegisterContainer(string typeName, Func<object> factory, params string[] optionalFields)
        => Register(typeName, factory, () => BuildSimpleContainer(optionalFields), isContainer: true);

    private static JsonSchema BuildSimpleContainer(params string[] optionalFields)
        => GetOrBuildSchema("controls" + (optionalFields.Length > 0 ? "," + string.Join(",", optionalFields) : string.Empty));

    private static JsonSchema GetOrBuildSchema(string cacheKey, Func<JsonSchema>? builder = null)
    {
        if (_schemaCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var schema = builder?.Invoke()
            ?? ContractBuilder.BaseControl().WithOptional(cacheKey.Split(',')).Build();
        _schemaCache[cacheKey] = schema;
        return schema;
    }

}
