import {
  getLayers,
  getLayerName,
  groupModulesByLayer,
  formatOverview,
  formatDetailResult,
  formatLayerResults,
  formatSearchResults,
  formatDependenciesResult,
  formatLayerMap,
  clearCache,
} from "../../../CsprojContext/src/core/utils";
import type { ModuleRule } from "../../../CsprojContext/src/core/types";

beforeEach(() => {
  clearCache();
});

describe("getLayers", () => {
  it("应返回所有层定义", () => {
    const layers = getLayers();
    expect(layers.length).toBeGreaterThan(0);
    expect(layers[0]).toHaveProperty("id");
    expect(layers[0]).toHaveProperty("name");
    expect(layers[0]).toHaveProperty("description");
  });

  it("层 ID 应从 0 开始递增", () => {
    const layers = getLayers();
    layers.forEach((layer: any, index: number) => {
      expect(layer.id).toBe(index);
    });
  });
});

describe("getLayerName", () => {
  it("应返回正确的层名称", () => {
    expect(getLayerName(0)).toBe("辅助/测试");
    expect(getLayerName(1)).toBe("基础层 (BasicMethod)");
  });

  it("未知层应返回默认文本", () => {
    expect(getLayerName(99)).toBe("未知层 (99)");
  });
});

describe("groupModulesByLayer", () => {
  it("应按层 ID 分组并排序", () => {
    const modules: ModuleRule[] = [
      {
        path: "test2.csproj",
        title: "模块2",
        layer: 2,
        description: "描述2",
        keywords: "",
        rules: [],
        prompts: [],
        hasModuleRule: true,
      },
      {
        path: "test1.csproj",
        title: "模块1",
        layer: 1,
        description: "描述1",
        keywords: "",
        rules: [],
        prompts: [],
        hasModuleRule: true,
      },
    ];

    const grouped = groupModulesByLayer(modules);
    expect(grouped.length).toBe(2);
    expect(grouped[0].layerId).toBe(1);
    expect(grouped[1].layerId).toBe(2);
  });

  it("空数组应返回空结果", () => {
    const grouped = groupModulesByLayer([]);
    expect(grouped.length).toBe(0);
  });
});

describe("formatOverview", () => {
  const mockModules: ModuleRule[] = [
    {
      path: "test.csproj",
      title: "测试模块",
      layer: 1,
      description: "测试描述",
      keywords: "测试;关键词",
      rules: ["规则1"],
      prompts: ["提示1"],
      hasModuleRule: true,
    },
  ];

  it("应生成概览 Markdown（简洁模式）", () => {
    const result = formatOverview(mockModules, false);
    expect(result).toContain("项目架构概览");
    expect(result).toContain("测试模块");
    expect(result).toContain("测试描述");
    expect(result).not.toContain("规则");
    expect(result).not.toContain("提示");
  });

  it("应生成概览 Markdown（详细模式）", () => {
    const result = formatOverview(mockModules, true);
    expect(result).toContain("规则1");
    expect(result).toContain("提示1");
    expect(result).toContain("测试;关键词");
  });

  it("空数组应返回正确格式", () => {
    const result = formatOverview([], false);
    expect(result).toContain("总计");
    expect(result).toContain("0");
  });
});

describe("formatDetailResult", () => {
  const mockModule: ModuleRule = {
    path: "test.csproj",
    title: "测试模块",
    layer: 1,
    description: "测试描述",
    keywords: "测试",
    rules: ["规则1", "规则2"],
    prompts: ["提示1"],
    hasModuleRule: true,
  };

  it("应生成详情 Markdown", () => {
    const result = formatDetailResult(mockModule, getLayerName);
    expect(result).toContain("测试模块");
    expect(result).toContain("基础层");
    expect(result).toContain("规则1");
    expect(result).toContain("规则2");
    expect(result).toContain("提示1");
  });
});

describe("formatLayerResults", () => {
  const mockModules: ModuleRule[] = [
    {
      path: "test.csproj",
      title: "测试模块",
      layer: 1,
      description: "测试描述",
      keywords: "测试",
      rules: [],
      prompts: [],
      hasModuleRule: true,
    },
  ];

  it("应生成层内模块列表 Markdown", () => {
    const result = formatLayerResults(mockModules, 1, getLayerName);
    expect(result).toContain("基础层");
    expect(result).toContain("测试模块");
    expect(result).toContain("1 个项目");
  });
});

describe("formatSearchResults", () => {
  const mockModules: ModuleRule[] = [
    {
      path: "test.csproj",
      title: "测试模块",
      layer: 1,
      description: "测试描述",
      keywords: "测试",
      rules: [],
      prompts: [],
      hasModuleRule: true,
    },
  ];

  it("应生成搜索结果 Markdown", () => {
    const result = formatSearchResults(mockModules, "测试", getLayerName);
    expect(result).toContain("搜索");
    expect(result).toContain("测试");
    expect(result).toContain("1 个匹配");
  });

  it("空结果应返回正确格式", () => {
    const result = formatSearchResults([], "空", getLayerName);
    expect(result).toContain("0 个匹配");
  });
});

describe("formatDependenciesResult", () => {
  it("应生成依赖关系 Markdown", () => {
    const result = formatDependenciesResult({
      path: "test.csproj",
      title: "测试模块",
      dependencies: ["dep1.csproj", "dep2.csproj"],
      dependencyCount: 2,
    });
    expect(result).toContain("测试模块");
    expect(result).toContain("dep1.csproj");
    expect(result).toContain("dep2.csproj");
    expect(result).toContain("2");
  });

  it("null 应返回错误信息", () => {
    const result = formatDependenciesResult(null);
    expect(result).toContain("不存在");
  });

  it("无依赖应返回正确信息", () => {
    const result = formatDependenciesResult({
      path: "test.csproj",
      title: "测试模块",
      dependencies: [],
      dependencyCount: 0,
    });
    expect(result).toContain("无 ProjectReference 依赖");
  });
});

describe("formatLayerMap", () => {
  it("应生成层映射表 Markdown", () => {
    const layers = [
      { id: 0, name: "测试层", description: "测试描述" },
    ];
    const result = formatLayerMap(layers);
    expect(result).toContain("层编号映射表");
    expect(result).toContain("测试层");
    expect(result).toContain("测试描述");
    expect(result).toContain("| 编号 | 层名称 | 说明 |");
  });
});
