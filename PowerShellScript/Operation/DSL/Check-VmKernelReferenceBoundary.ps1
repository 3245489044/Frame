# Check-VmKernelReferenceBoundary.ps1
# VM 内核引用面门禁（VM-P6-d，对齐 Vm.csproj 头注释「按面分层」与
# Docs\analysis\2026\10\10.8\2026-10-08-Vm框架-运行期编译就绪-设计与实施.md §6-1/§7-2）。
# 判据：内核热路径（队列 / 循环 / 实例 / 路由 / 换代 / 装载 / 通用换代服务）**零编译依赖**——
#       不得引用 Roslyn（Microsoft.CodeAnalysis）或编译面类型（VmCompiler / CSharpCompilation）。
#       VM 由「零外部依赖」变为「仅编译面依赖 Roslyn」是显式承认的代价；本门禁把该分层固定为机器判据：
#       新增内核文件引用编译面即红（防编译依赖渗回热路径）。
#   - 报告模式（默认）：列出命中，不设退出码。
#   - 校验模式（-Check）：命中 > 0 → 退出码 1（CI 门禁）。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$coreDir = Join-Path $Root 'Projects\Vm\Core'
if (-not (Test-Path $coreDir)) { Write-Error "未找到 Vm 内核目录: $coreDir"; exit 2 }

# 内核热路径文件（须零编译依赖；新增内核文件须加入本清单）
$kernelFiles = @(
    'VmSignalQueue.cs',
    'VmLoop.cs',
    'VmInstance.cs',
    'VmSignalRouter.cs',
    'VmGenerationMachine.cs',
    'VmGenerationController.cs',
    'VmGenerationRegistry.cs',
    'VmGenerationContext.cs',
    'VmSwapPorts.cs',
    'VmProgramSwapService.cs',
    'VmProgramLoader.cs'
)

# 编译面禁用符号（**大小写敏感 + 词边界**：`VmCompiler` 不得误命中编译请求 DTO `VmCompileRequest`）
$patterns = [ordered]@{
    'Microsoft\.CodeAnalysis' = 'Roslyn（编译面专有；内核热路径禁用）'
    '\bCSharpCompilation\b'   = 'Roslyn 编译对象（内核热路径禁用）'
    '\bVmCompiler\b'          = '编译面类型（内核热路径禁用；DTO VmCompileRequest/VmCompileResult 不属禁列）'
}

$hits = [System.Collections.Generic.List[object]]::new()
$scanned = 0
foreach ($name in $kernelFiles) {
    $path = Join-Path $coreDir $name
    if (-not (Test-Path $path)) { continue }
    $scanned++
    $lines = Get-Content $path -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $text = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $trimmed = $text.Trim()
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('/*') -or $trimmed.StartsWith('*')) { continue }
        foreach ($symbol in $patterns.Keys) {
            if ($text -cnotmatch $symbol) { continue }
            $hits.Add([pscustomobject]@{
                File = $path.Substring($Root.Length).TrimStart('\')
                Line = $i + 1
                Pattern = $symbol
                Text = $trimmed
            })
            break
        }
    }
}

Write-Host '=== [VM-P6-d] 内核热路径零编译依赖（禁 Roslyn / VmCompiler） ==='
Write-Host ("扫描内核文件: {0} | 违规命中: {1}" -f $scanned, $hits.Count)
if ($hits.Count -gt 0) {
    Write-Host '--- 命中清单（文件:行 符号 片段） ---'
    $hits | ForEach-Object { Write-Host ("  [违规] {0}:{1} [{2}] {3}" -f $_.File, $_.Line, $_.Pattern, $_.Text) }
}

if ($Check -and $hits.Count -gt 0) {
    Write-Host '校验失败：VM 内核热路径出现编译依赖（按面分层被破坏）。编译须唯经 VmCompiler（编译面）。'
    exit 1
}
exit 0
