using System.Collections.Generic;

namespace RoslynSpace.RoslynStringCodeGeneration;

public class StaticStringClassModel
{
    public Dictionary<string, string> Fields { get; set; } = new();
    public List<string> AdditionalUsings { get; set; } = new();
}
