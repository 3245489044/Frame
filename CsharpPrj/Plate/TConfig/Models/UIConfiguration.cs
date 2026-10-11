
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

    /// <summary>
    /// 结构化初始化命令（B3 / INV-2）：携带 <c>requires</c> 相位/能力前置；缺省 null = 走旧 <see cref="InitCommands"/> 字符串通道（零回归）。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<InitCommandSpec>? InitCommandSpecs { get; set; }

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

/// <summary>
/// 结构化初始化命令条目（B3）：exec 符号 + output 目标 + requires 相位/能力前置。
/// <para>requires 缺省 null = 无门（立即执行）；未满足 ⇒ 登记 pending，相位跃迁后幂等重放。</para>
/// </summary>
public class InitCommandSpec
{
    /// <summary>命令体（符号名或 `exec &lt;符号&gt; [k=v...]`；缺省 exec 前缀由执行器补齐）。</summary>
    public string Exec { get; set; } = string.Empty;

    /// <summary>输出写回目标控件名（等价 `output=&lt;Control&gt;`）；缺省 null = 不写回。</summary>
    public string? Output { get; set; }

    /// <summary>前置判定项（如 <c>phase&gt;=FrameworkAssembled</c> / <c>capability:agent.conversation</c>）；缺省 null = 无门。</summary>
    public List<string>? Requires { get; set; }
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
