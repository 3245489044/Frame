/**
 * 辅助函数：模拟 parseModuleRule 的行为，直接解析字符串内容
 */
function parseModuleRuleFromString(filePath: string, content: string) {
  if (!content.includes('Label="ModuleRule"')) return null;

  const result: any = {
    path: filePath,
    title: "",
    layer: -1,
    description: "",
    keywords: "",
    rules: [],
    prompts: [],
    hasModuleRule: true,
  };

  const titleMatch = content.match(/<ModuleRule_Title>(.*?)<\/ModuleRule_Title>/);
  if (titleMatch) result.title = titleMatch[1];

  const layerMatch = content.match(/<ModuleRule_Layer>(.*?)<\/ModuleRule_Layer>/);
  if (layerMatch) result.layer = parseInt(layerMatch[1], 10);

  const descMatch = content.match(/<ModuleRule_Description>(.*?)<\/ModuleRule_Description>/);
  if (descMatch) result.description = descMatch[1];

  const kwMatch = content.match(/<ModuleRule_Keywords>(.*?)<\/ModuleRule_Keywords>/);
  if (kwMatch) result.keywords = kwMatch[1];

  const rulesRegex = /<ModuleRule_Rule Include="(.*?)"/g;
  let ruleMatch;
  while ((ruleMatch = rulesRegex.exec(content)) !== null) {
    result.rules.push(ruleMatch[1]);
  }

  const promptsRegex = /<ModuleRule_Prompt Include="(.*?)"/g;
  let promptMatch;
  while ((promptMatch = promptsRegex.exec(content)) !== null) {
    result.prompts.push(promptMatch[1]);
  }

  return result;
}

/**
 * 辅助函数：模拟从 .csproj 内容中提取 ProjectReference 依赖
 */
function extractProjectReferences(content: string): string[] {
  const refRegex = /<ProjectReference Include="([^"]+)"/g;
  const deps: string[] = [];
  let match;
  while ((match = refRegex.exec(content)) !== null) {
    deps.push(match[1]);
  }
  return deps;
}

describe("parseModuleRule（解析逻辑）", () => {
  it("应解析完整的 ModuleRule 属性", () => {
    const csprojContent = `<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup Label="ModuleRule">
    <ModuleRule_Title>测试模块</ModuleRule_Title>
    <ModuleRule_Layer>1</ModuleRule_Layer>
    <ModuleRule_Description>这是一个测试模块</ModuleRule_Description>
    <ModuleRule_Keywords>测试;模块;示例</ModuleRule_Keywords>
  </PropertyGroup>
  <ItemGroup Label="ModuleRule_Rules">
    <ModuleRule_Rule Include="规则1" />
    <ModuleRule_Rule Include="规则2" />
  </ItemGroup>
  <ItemGroup Label="ModuleRule_Prompts">
    <ModuleRule_Prompt Include="提示1" />
  </ItemGroup>
</Project>`;

    const result = parseModuleRuleFromString("test.csproj", csprojContent);
    expect(result).not.toBeNull();
    expect(result!.title).toBe("测试模块");
    expect(result!.layer).toBe(1);
    expect(result!.description).toBe("这是一个测试模块");
    expect(result!.keywords).toBe("测试;模块;示例");
    expect(result!.rules).toEqual(["规则1", "规则2"]);
    expect(result!.prompts).toEqual(["提示1"]);
    expect(result!.hasModuleRule).toBe(true);
  });

  it("无 ModuleRule 时应返回 null", () => {
    const csprojContent = `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>`;

    const result = parseModuleRuleFromString("test.csproj", csprojContent);
    expect(result).toBeNull();
  });

  it("部分属性缺失时应使用默认值", () => {
    const csprojContent = `<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup Label="ModuleRule">
    <ModuleRule_Title>部分模块</ModuleRule_Title>
    <ModuleRule_Layer>2</ModuleRule_Layer>
  </PropertyGroup>
</Project>`;

    const result = parseModuleRuleFromString("test.csproj", csprojContent);
    expect(result).not.toBeNull();
    expect(result!.title).toBe("部分模块");
    expect(result!.layer).toBe(2);
    expect(result!.description).toBe("");
    expect(result!.keywords).toBe("");
    expect(result!.rules).toEqual([]);
    expect(result!.prompts).toEqual([]);
    expect(result!.hasModuleRule).toBe(true);
  });

  it("应处理特殊字符", () => {
    const csprojContent = `<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup Label="ModuleRule">
    <ModuleRule_Title>模块 A & B</ModuleRule_Title>
    <ModuleRule_Layer>3</ModuleRule_Layer>
    <ModuleRule_Description>描述 with <tag></ModuleRule_Description>
    <ModuleRule_Keywords>key1;key2</ModuleRule_Keywords>
  </PropertyGroup>
</Project>`;

    const result = parseModuleRuleFromString("test.csproj", csprojContent);
    expect(result).not.toBeNull();
    expect(result!.title).toBe("模块 A & B");
    expect(result!.description).toBe("描述 with <tag>");
  });
});

describe("extractProjectReferences（依赖提取逻辑）", () => {
  it("应解析 ProjectReference 依赖", () => {
    const csprojContent = `<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\\BasicMethod\\BasicMethod.csproj" />
    <ProjectReference Include="..\\Share\\Share.csproj" />
  </ItemGroup>
</Project>`;

    const result = extractProjectReferences(csprojContent);
    expect(result).toEqual([
      "..\\BasicMethod\\BasicMethod.csproj",
      "..\\Share\\Share.csproj",
    ]);
  });

  it("无 ProjectReference 时应返回空数组", () => {
    const csprojContent = `<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" />
  </ItemGroup>
</Project>`;

    const result = extractProjectReferences(csprojContent);
    expect(result).toEqual([]);
  });

  it("空内容应返回空数组", () => {
    const result = extractProjectReferences("");
    expect(result).toEqual([]);
  });

  it("应处理多种路径格式", () => {
    const csprojContent = `<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\\Modules\\Modules.csproj" />
    <ProjectReference Include="..\\Share\\Share.csproj" />
    <ProjectReference Include="..\\Operation\\Operation.csproj" />
  </ItemGroup>
</Project>`;

    const result = extractProjectReferences(csprojContent);
    expect(result.length).toBe(3);
    expect(result[0]).toContain("Modules");
    expect(result[1]).toContain("Share");
    expect(result[2]).toContain("Operation");
  });
});
