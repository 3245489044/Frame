
namespace Modules.Transformers
{
    public interface ITransformMetadata
    {
        string Name { get; }

        Type InputType { get; }

        Type OutputType { get; }

        string? Description { get; }

        string Version { get; }

        IReadOnlyDictionary<string, string> Properties { get; }
    }

    public class MetadataTransformer<TInput, TOutput> : ITransformer<TInput, TOutput>, ITransformMetadata
    {
        private readonly ITransformer<TInput, TOutput> _inner;
        private readonly ITransformMetadata _metadata;

        public string Name => _metadata.Name;


        public Type InputType => _metadata.InputType;
        public Type OutputType => _metadata.OutputType;
        public string? Description => _metadata.Description;
        public string Version => _metadata.Version;
        public IReadOnlyDictionary<string, string> Properties => _metadata.Properties;

        public MetadataTransformer(ITransformer<TInput, TOutput> inner, ITransformMetadata metadata)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        public TOutput Transform(TInput input) => _inner.Transform(input);
    }

    public class TransformMetadataBuilder
    {
        private readonly string _name;
        private readonly Type _inputType;
        private readonly Type _outputType;
        private string? _description;
        private string _version = "1.0.0";
        private Dictionary<string, string> _properties = new();

        public TransformMetadataBuilder(string name, Type inputType, Type outputType)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _inputType = inputType ?? throw new ArgumentNullException(nameof(inputType));
            _outputType = outputType ?? throw new ArgumentNullException(nameof(outputType));
        }

        public TransformMetadataBuilder WithDescription(string? description)
        {
            _description = description;
            return this;
        }

        public TransformMetadataBuilder WithVersion(string version)
        {
            _version = version;
            return this;
        }

        public TransformMetadataBuilder WithProperty(string key, string value)
        {
            _properties[key] = value;
            return this;
        }

        public TransformMetadataBuilder WithProperties(Dictionary<string, string> properties)
        {
            _properties = properties;
            return this;
        }

        public ITransformMetadata Build()
            => new TransformMetadataImpl
            {
                Name = _name,
                InputType = _inputType,
                OutputType = _outputType,
                Description = _description,
                Version = _version,
                Properties = _properties.AsReadOnly()
            };

        public static ITransformMetadata FromTransformer<TInput, TOutput>(
            ITransformer<TInput, TOutput> transformer,
            string? description = null,
            string version = "1.0.0")
            => new TransformMetadataImpl
            {
                Name = transformer.Name,
                InputType = typeof(TInput),
                OutputType = typeof(TOutput),
                Description = description,
                Version = version,
                Properties = new Dictionary<string, string>().AsReadOnly()
            };

        public static ITransformMetadata FromAsyncTransformer<TInput, TOutput>(
            IAsyncTransformer<TInput, TOutput> transformer,
            string? description = null,
            string version = "1.0.0")
            => new TransformMetadataImpl
            {
                Name = transformer.Name,
                InputType = typeof(TInput),
                OutputType = typeof(TOutput),
                Description = description,
                Version = version,
                Properties = new Dictionary<string, string>().AsReadOnly()
            };
    }

    internal class TransformMetadataImpl : ITransformMetadata
    {
        public string Name { get; init; } = string.Empty;
        public Type InputType { get; init; } = typeof(object);
        public Type OutputType { get; init; } = typeof(object);
        public string? Description { get; init; }
        public string Version { get; init; } = "1.0.0";
        public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
    }
}
