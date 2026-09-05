using Common.PlaceholderFramework.SchemaValidation;
using FrontEndDriven.Shared;

namespace FrontEndDriven.Shared
{
    public class ContractBuilder
    {
        private readonly JsonSchemaBuilder _builder = new();

        public static readonly string[] CommonOptionalFields =
            EventHelper.CommonOptionalFieldsWithEvents;

        private static HashSet<string> DockAllowedValues => FrontEndDriven.FrontEndDrivenConstants.DockAllowedValues;

        private static HashSet<string> AnchorAllowedValues => FrontEndDriven.FrontEndDrivenConstants.AnchorAllowedValues;

        /// <summary>可见性语义声明合法值（Phase ②，单一真源见 FrontEndDrivenConstants.HiddenSemanticsAllowedValues；消费方：ConfigDrivenControlFactory.TryParseVisibilityIntent）。</summary>
        private static HashSet<string> HiddenSemanticsAllowedValues => FrontEndDriven.FrontEndDrivenConstants.HiddenSemanticsAllowedValues;


        public ContractBuilder WithRequired(params string[] fields)
        {
            _builder.WithRequired(fields);
            return this;
        }

        public ContractBuilder WithOptional(params string[] fields)
        {
            _builder.WithOptional(fields);
            return this;
        }

        public ContractBuilder WithConditional(string condition, string[] fields)
        {
            _builder.WithConditional(condition, fields);
            return this;
        }

        public ContractBuilder WithAllowedValues(string field, HashSet<string> allowedValues)
        {
            _builder.WithAllowedValues(field, allowedValues);
            return this;
        }

        public ContractBuilder WithDockAllowed()
        {
            _builder.WithAllowedValues("dock", DockAllowedValues);
            return this;
        }

        public ContractBuilder WithAnchorAllowed()
        {
            _builder.WithAllowedValues("anchor", AnchorAllowedValues);
            return this;
        }

        public JsonSchema Build() => _builder.Build();


        public static ContractBuilder BaseControl()
        {
            return new ContractBuilder()
                .WithRequired("type", "name")
                .WithOptional(CommonOptionalFields)
                .WithOptional("hiddenSemantics")
                .WithAllowedValues("hiddenSemantics", HiddenSemanticsAllowedValues)
                .WithDockAllowed();
        }

        public static ContractBuilder ContainerControl()
        {
            return BaseControl()
                .WithOptional("controls");
        }


        public static JsonSchema DefaultContract => BaseControl().Build();
    }
}