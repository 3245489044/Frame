
using System.Text.Json.Serialization;

namespace TConfig;

public class UIConfiguration
{
    public string? Extends { get; set; }
    public FormConfig Form { get; set; } = new();
    public NavigationConfig Navigation { get; set; } = new();
    public List<ViewConfig> Views { get; set; } = [];
    public List<DataBindingConfig> DataBindings { get; set; } = [];
    public StyleConfig Styles { get; set; } = new();
    public DataLoadConfig? DataLoad { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BootPhase>? Boot { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LifecycleHooks? Lifecycle { get; set; }

    public List<string>? InitCommands { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InitCommandMode { get; set; }

    /// <summary>命名 HTML 模板集（供 HTML 后端模板引擎渲染数据片段；键=模板名，值=模板字符串）。
    /// 缺省 null = 无模板，消费方回退内嵌默认。把 HTML 外置进 JSON、C# 只产数据。</summary>
    [JsonPropertyName("templates")]
    public Dictionary<string, string>? Templates { get; set; }
}

public class BootPhase
{
    public string Id { get; set; } = string.Empty;

    public string Load { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
}

public class LifecycleHooks
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? OnBeforeLoad { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? OnAfterLoad { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? OnBeforeDestroy { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? OnAfterDestroy { get; set; }
}

public enum ComponentType
{
    Panel,
    Button,
    TextBox,
    Label,
    ListView,
    ComboBox,
    CheckBox,
    GroupBox,
    TabControl,
    TabPage
}
