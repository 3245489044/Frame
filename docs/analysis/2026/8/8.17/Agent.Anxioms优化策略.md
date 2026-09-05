核对完类型签名（`DslDeclaration.RawLine`、`AxiomCodeGeneratorOptions.LevelOverrides`、`AxiomLevelExtensions.TryParse`、`AxiomCodeGenerator` 的映射写出位置），给出精确到代码的优化方案。

## 方案：把"新增一条公理"从 10+ 步降到 3 步

核心思路：**生成器永远用 `--include <Name>` 定向生成（杜绝全量重生成的 100 文件坑）+ `--include` 时合并而非覆盖 mapping-report（杜绝手工合并的坑）+ 契约内显式 `@level/@axis`（杜绝猜节标题/名字的坑）**。三级改动如下。

---

### A. 生成器映射合并（必做，解决最大 token 黑洞）

`CsharpPrj/Plate/DslMethod/CodeGen/AxiomCodeGenerator.cs` 第 76-79 行，把无条件覆盖改为**按符号名合并（去重）**：

```csharp
if (opts.EmitMappingReport && result.Count > 0)
{
    var reportPath = Path.Combine(outDir, "mapping-report.txt");
    var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
    if (File.Exists(reportPath))
        foreach (var line in File.ReadAllLines(reportPath))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                merged[parts[1].Trim()] = line;   // key = 符号名（tab 第 2 段）
        }
    foreach (var m in mapping)                      // 新生成条目按名覆盖旧条目
    {
        var parts = m.Split('\t');
        if (parts.Length >= 2) merged[parts[1].Trim()] = m;
    }
    File.WriteAllLines(reportPath, merged.Values, Encoding.UTF8);
}
```

效果：`--include X` 只新增/更新 X 的映射行，既有 15 行原样保留。全量（`IncludeNames=null`）走同逻辑也安全（合并即全集）。

---

### B. 契约内显式 `@level/@axis`（消除启发式猜测）

1. `AxiomCodeGenerator.cs` 解析显式标注（读 `decl.RawLine`，不依赖解析器字段）：
   ```csharp
   // CanonicalLevel 之前：先查显式 @level，再回落 LevelOverrides/节标题
   var explicitLevel = Regex.Match(chosen.RawLine, @"@level\s*:\s*(\w[\w.]*)", RegexOptions.IgnoreCase);
   if (explicitLevel.Success && AxiomLevelExtensions.TryParse(explicitLevel.Groups[1].Value, out var lv))
       canonical = lv;
   // axis：先查显式 @axis，再回落 AxisOverrides，最后 AxisOf(name)
   var explicitAxis = Regex.Match(chosen.RawLine, @"@axis\s*:\s*([\w-]+)", RegexOptions.IgnoreCase);
   var axis = explicitAxis.Success ? explicitAxis.Groups[1].Value
            : (opts.AxisOverrides?.TryGetValue(name, out var a) == true ? a : AxisOf(name));
   ```
2. `AxiomCodeGeneratorOptions` 增 `IReadOnlyDictionary<string, string>? AxisOverrides`（与 `LevelOverrides` 对称）。
3. `Axioms.Generator/Program.cs` 增 `--axes Name=infra,Name2=output` 参数（复用现有 `--overrides` 的解析循环）。

契约写法变为自解释：
```text
[AX] DerivedDataAutoMaintenance: ... | @level:L2 | @axis:infra | irr:"structural"
```

---

### C. 一条命令封装（可选，最高操作收益）

`PowerShellScript/Axioms/Add-Axiom.ps1`（新脚本）：
```powershell
param(
  [Parameter(Mandatory)][string]$Name,
  [string]$Body,                                   # 公理公式（唯一需人工写的）
  [ValidateSet('L0','L0.5','L1','L2','L3')][string]$Level,
  [string]$Axis = 'infra',
  [string]$Prompt = 'Agent/Config/prompts/logic-axioms.system.md',
  [string]$Section = '## B5 L2 领域公理（可版本化）'
)
# 1) 在 $Prompt 的 $Section 末追加 `[AX] $Name: $Body | @level:$Level | @axis:$Axis`
# 2) dotnet run --project Agent/Axioms.Generator -- Agent/Config/prompts Agent/Axioms --include $Name
# 3) 打印生成路径 + mapping 增量
```
（Body 仍需你写，但脚本把"改契约 + 跑生成器 + 合并 mapping"收敛成一次调用。）

---

## 新工作流（A+B 落地后）

| 步骤 | 操作 |
|------|------|
| 1 | 在 `Add-Axiom.ps1`（或直接）写契约行：`[AX] Name: 公式 \| @level:L2 \| @axis:infra` |
| 2 | `dotnet run --project Agent\Axioms.Generator -- Agent\Config\prompts Agent\Axioms --include Name`（**定向**，永不全量） |
| 3 | 生成 `Agent\Axioms\L2\Name.cs` + mapping 自动合并 ✓ |

→ **只写 1 行 + 跑 1 次，无手工合并、无 100 文件清理、无猜层级/轴。** 比本次的往返省约 90% token。

---

## 影响面

- 仅改 `AxiomCodeGenerator.cs`（合并 + 显式标注解析）+ `AxiomCodeGeneratorOptions` + `Program.cs`（`--axes`），共 3 个文件。
- 不改 `DslParser`（走 `RawLine` 正则，向后兼容）；不改既有 15 条公理；构建面 = `DslMethod.csproj`。
- `mapping-report.txt` 从"生成器产物（可覆盖）"变为"生成器合并产物"，对既有条目幂等。

要我按 A → B → C 落地吗？还是先只做 A（最小改动、立即消除本次 80% 的浪费）？