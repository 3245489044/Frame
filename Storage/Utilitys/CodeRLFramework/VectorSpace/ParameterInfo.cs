namespace CodeRLFramework.VectorSpace
{
    public class ParameterInfo
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public bool IsOptional { get; set; }

        public string? DefaultValue { get; set; }

        public override string ToString()
        {
            return $"{Type} {Name}";
        }
    }
}
