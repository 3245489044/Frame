namespace CodeRLFramework.Input
{
    public class TemplateInput
    {
        public string Code { get; set; } = string.Empty;

        public double Weight { get; set; } = 0.3;

        public string Placeholder { get; set; } = "// TODO";

        public bool PreserveStructure { get; set; } = true;

        public void LoadFromFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                Code = File.ReadAllText(filePath);
            }
        }

        public bool HasPlaceholder()
        {
            return !string.IsNullOrEmpty(Placeholder) && Code.Contains(Placeholder);
        }
    }
}
