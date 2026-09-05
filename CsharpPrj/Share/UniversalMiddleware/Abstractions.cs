#nullable enable
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandFramework.Parsing;
using CommandFramework.Models;

namespace UniversalMiddleware.Abstractions
{
    public interface IJsonTransform
    {
        string Name { get; }
        Task<object?> ProcessAsyncUntyped(object? input, JsonPipelineContext context);
    }

    public interface IJsonTransform<TInput, TOutput> : IJsonTransform
    {
        Task<TOutput> ProcessAsync(TInput input, JsonPipelineContext context);
    }

    public delegate Task<JsonCommandResult> JsonMiddlewareDelegate(JsonCommand command);

    public interface IJsonSerializer
    {
        string Serialize<T>(T obj, JsonSerializerOptions? options = null);
        T? Deserialize<T>(string json, JsonSerializerOptions? options = null);
        string SafeSerialize<T>(T obj, JsonSerializerOptions? options = null);
        T? DeserializeWithValidation<T>(string json, Action<JsonValidationResult>? onValidate = null);
        Task SerializeToFileAsync<T>(string path, T obj, JsonSerializerOptions? options = null);
        Task<T?> DeserializeFromFileAsync<T>(string path, JsonSerializerOptions? options = null);
    }

    public interface IJsonValidator
    {
        JsonValidationResult ValidateSyntax(string json);
        JsonValidationResult ValidateStructure<T>(string json);
        JsonValidationResult ValidateModel<T>(T model);
    }

    public interface IJsonPathEngine
    {
        JsonNode? Navigate(JsonNode root, string path);
        bool TryNavigate(JsonNode root, string path, out JsonNode? result);
        bool TryGetValue<T>(JsonNode root, string path, out T? value);
        JsonNode? SetValue(JsonNode root, string path, JsonNode? value);
        Task<JsonNode?> QueryAsync(JsonNode root, string pathExpression);
        IEnumerable<string> GetPaths(JsonNode root, string? filterPattern = null);
    }

    public class JsonValidationResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }

        public JsonValidationResult(bool isValid, IEnumerable<string>? errors = null, IEnumerable<string>? warnings = null)
        {
            IsValid = isValid;
            Errors = errors?.ToList().AsReadOnly() ?? Array.Empty<string>().AsReadOnly();
            Warnings = warnings?.ToList().AsReadOnly() ?? Array.Empty<string>().AsReadOnly();
        }

        public static JsonValidationResult Success() => new(true);
        public static JsonValidationResult Failure(params string[] errors) => new(false, errors);
    }

    public class JsonPipelineConfig
    {
        public string Name { get; set; } = "DefaultPipeline";
        public bool ThrowOnError { get; set; } = false;
        public bool EnableCaching { get; set; } = false;
        public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(5);
        public Dictionary<string, object> StageConfigs { get; set; } = new();
    }

    public class JsonPipelineContext : Modules.Pipeline.PipelineContext
    {
        public JsonPipelineConfig Config { get; set; } = new();

        public Microsoft.Extensions.Logging.ILogger? Logger { get; set; }

        public JsonPipelineContext(CancellationToken externalToken = default, IServiceProvider? services = null)
            : base(externalToken, services) { }
    }

    public interface IMediator
    {
        ICacheManager Cache { get; }
        IStateManager State { get; }

        Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class;

        Task PublishAsync<TEvent>(TEvent @event) where TEvent : class;

        void UnregisterAll();
    }

    public interface ICacheManager
    {
        T? Get<T>(string key);
        void Set<T>(string key, T value, TimeSpan? expiration = null);
        void Remove(string key);
        void Clear();
        bool Contains(string key);
    }

    public interface IStateManager
    {
        T? GetState<T>(string key);
        void SetState<T>(string key, T value);
        void RemoveState(string key);
        void ClearState();
        bool ContainsState(string key);
        event EventHandler<StateChangedEventArgs>? StateChanged;
    }

    public class StateChangedEventArgs : EventArgs
    {
        public string Key { get; }
        public object? OldValue { get; }
        public object? NewValue { get; }

        public StateChangedEventArgs(string key, object? oldValue, object? newValue)
        {
            Key = key; OldValue = oldValue; NewValue = newValue;
        }
    }




    public class LayoutPosition
    {
        public double Left { get; set; } = -1;
        public double Top { get; set; } = -1;
        public double Width { get; set; } = -1;
        public double Height { get; set; } = -1;
    }

    public class SizeConstraints
    {
        public int MinWidth { get; set; } = 0;
        public int MinHeight { get; set; } = 0;
        public int MaxWidth { get; set; } = int.MaxValue;
        public int MaxHeight { get; set; } = int.MaxValue;
        public bool MaintainAspectRatio { get; set; } = false;
    }

    public class ZOrder
    {
        public int Order { get; set; } = 0;
        public bool BringToFront { get; set; } = false;
    }

    public class ElementDefinition
    {
        public string ControlName { get; set; } = string.Empty;
        public LayoutPosition Position { get; set; } = new();
        public SizeConstraints SizeConstraints { get; set; } = new();
        public ZOrder ZOrder { get; set; } = new();
    }

    public class LayoutDefinition
    {
        public string Name { get; set; } = string.Empty;
        public global::UniversalLayoutFramework.LayoutType Type { get; set; } = global::UniversalLayoutFramework.LayoutType.Proportional;
        public List<ElementDefinition> Elements { get; set; } = new();
        public LayoutConstraints Constraints { get; set; } = new();
    }

    public class LayoutConstraints
    {
        public int MinWidth { get; set; } = 0;
        public int MinHeight { get; set; } = 0;
        public int MaxWidth { get; set; } = int.MaxValue;
        public int MaxHeight { get; set; } = int.MaxValue;
        public bool AllowOverflow { get; set; } = false;
    }

    public class LayoutInput
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int ParentWidth { get; }
        public int ParentHeight { get; }

        public LayoutInput(int x, int y, int width, int height, int parentWidth, int parentHeight)
        {
            X = x; Y = y; Width = width; Height = height;
            ParentWidth = parentWidth; ParentHeight = parentHeight;
        }
    }

    public class ElementOutput
    {
        public string ControlName { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ZOrder { get; set; }
    }

    public class LayoutOutput
    {
        public bool IsValid { get; }
        public string? ErrorMessage { get; }
        public List<ElementOutput>? Elements { get; }

        public LayoutOutput(bool isValid, string? errorMessage, List<ElementOutput>? elements = null)
        {
            IsValid = isValid; ErrorMessage = errorMessage; Elements = elements;
        }

        public static LayoutOutput Success(List<ElementOutput> elements) => new(true, null, elements);
        public static LayoutOutput Failure(string error) => new(false, error);
    }

    public delegate List<ElementOutput> OverlapResolverDelegate(List<ElementOutput> elements, int parentWidth, int parentHeight);


    public class SmartLayoutConfig
    {
        public bool EnableResizeHandling { get; set; } = true;
        public bool Recursive { get; set; } = true;
        public bool LogEnabled { get; set; } = false;
        public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(50);
        public OverlapResolverDelegate? OverlapResolver { get; set; }
    }

    public class ControlDescriptor
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public Dictionary<string, object?> Properties { get; set; } = new();
        public List<ControlDescriptor> Children { get; set; } = new();
        public EventDescriptor? Events { get; set; }
        public ContainerConfig? Container { get; set; }
    }

    public class EventDescriptor
    {
        public string? Click { get; set; }
        public string? DoubleClick { get; set; }
        public string? TextChanged { get; set; }
        public string? SelectedIndexChanged { get; set; }
        public string? CheckedChanged { get; set; }
        public Dictionary<string, string> CustomEvents { get; set; } = new();
    }

    public interface ILayoutCalculator
    {
        LayoutOutput Calculate(LayoutDefinition definition, LayoutInput input);
        ElementOutput CalculateElement(ElementDefinition element, LayoutInput input);
    }

    public interface ILayoutValidator
    {
        JsonValidationResult ValidateDefinition(LayoutDefinition definition);
        JsonValidationResult ValidateElement(ElementDefinition element, LayoutDefinition context);
        JsonValidationResult ValidateConstraints(LayoutConstraints constraints);
    }


    public interface IDataBindingManager
    {
        void RegisterDataSource<T>(string key, T dataSource) where T : class, INotifyPropertyChanged;
        T? GetDataSource<T>(string key) where T : class;
        void UnregisterDataSource(string key);
        void BindProperty<T>(string dataSourceKey, string propertyName, Action<T> onChanged);
        void UnbindProperty(string dataSourceKey, string propertyName);
        void ClearAll();
    }

    public delegate Task<JsonCommandResult> JsonMiddlewareFunc(JsonCommand command, JsonMiddlewareDelegate next);

    public enum BindingMode
    {
        OneWay,
        TwoWay,
        OneWayToSource,
        OneTime
    }

    public interface ISmartLayoutEngine
    {
        void Apply(global::UniversalLayoutFramework.ILayoutContainer container, object? config = null);
        void Refresh(global::UniversalLayoutFramework.ILayoutContainer container);
        void Attach(global::UniversalLayoutFramework.ILayoutContainer container, object? config = null);
        void Detach(global::UniversalLayoutFramework.ILayoutContainer container);
    }


    public interface IUILayoutManager<TControl>
    {
        void RegisterLayout(string name, object config);
        void ApplyLayout(TControl parent, string layoutName);
        void ApplyLayoutConfig(TControl parent, object config);
        void ApplyDefaultStyle(TControl control);
        void ApplyDarkStyle(TControl control);
        void EnableDoubleBuffering(TControl control);
    }

    public interface IUIManager<TControl, TForm, TConfig>
    {
        object Mediator { get; }
        object Events { get; }
        object DataBinding { get; }
        object Config { get; }
        IControlFactory<TControl, TConfig> Controls { get; }
        IUILayoutManager<TControl> Layout { get; }
        IViewManager<TForm> Views { get; }
        void Initialize();
        void Shutdown();
    }

    public interface IControlFactory<TControl, TConfig>
    {
        TControl CreateControl(TConfig config, object mediator);
        TControl CreateControl(string type);
        TControl CreateControlInstance(string type);
    }

    public interface IViewManager<TForm>
    {
        void RegisterForm(TForm form);
        void UnregisterForm(string formName);
        void ShowForm(string formName);
        void HideForm(string formName);
        TForm? GetForm(string formName);
        void CloseForm(string formName);
        object? GetControl(string formName, string controlName);
        void NavigateTo(string viewName);
        void NavigateBack();
    }

    public interface IConfigDrivenFactory<TConfig, TProduct>
    {
        TProduct Create(TConfig config);
        IEnumerable<TProduct> CreateMany(IEnumerable<TConfig> configs);
    }




    public class ContainerEventArgs : EventArgs
    {
        public string ContainerName { get; }
        public string Action { get; }

        public ContainerEventArgs(string containerName, string action)
        {
            ContainerName = containerName;
            Action = action;
        }
    }

    public class EventsBoundEventArgs : EventArgs
    {
        public string TargetName { get; }
        public int EventCount { get; }
        public bool Success { get; }
        public string? ErrorMessage { get; }

        public EventsBoundEventArgs(string targetName, int eventCount, bool success, string? error = null)
        {
            TargetName = targetName;
            EventCount = eventCount;
            Success = success;
            ErrorMessage = error;
        }
    }
}



