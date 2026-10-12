<#
.SYNOPSIS
    DeclaredBundle 冻结产物生成器（G1）—— 声明面单点编译 → 三份冻结产物。

.DESCRIPTION
    唯一编译点 = Usharp.Adapters.Declarations.DeclaredBundleBuilder（经 Usharp.Bundle 工具调用）。
    两种用法：
      · -OutDir <dir>                      ：三份产物写入该目录（重生成提交产物用）。
      · -OutFile <file> -Kind <k>          ：只导出指定一份（供 Check-GeneratedArtifactsDrift 复算比对）。
    Kind：manifest（declared-manifest.json）/ roundspec（round-spec.generated.json）/ schema（reference-schema.generated.json）。

.PARAMETER ConfigRoot
    声明配置根（含 nodes / workflows 子目录；仓库相对或绝对）。

.PARAMETER OutDir
    产物输出目录（三份）。

.PARAMETER OutFile
    单份产物输出文件路径。

.PARAMETER Kind
    单份产物种类（与 -OutFile 联用）。

.EXAMPLE
    .\Generate-DeclaredBundle.ps1 -ConfigRoot 'Projects\Plug\Agent\Config' -OutDir 'Projects\Plug\Agent\src\Agent.Core\Declared'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ConfigRoot,
    [string]$OutDir = '',
    [string]$OutFile = '',
    [ValidateSet('manifest', 'roundspec', 'schema')][string]$Kind = 'manifest'
)

$ErrorActionPreference = 'Stop'

# 仓库根：本脚本位于 Frame\PowerShellScript\Share\Usharp → 上溯四级。
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path

# 工具程序集（须先构建 Frame\Usharp\src\Usharp.Bundle）。
$toolDll = Join-Path $root 'Frame\Usharp\BuildOutput\Debug\Usharp.Bundle.dll'
if (-not (Test-Path -LiteralPath $toolDll)) {
    Write-Error "Usharp.Bundle 未构建：$toolDll（先构建 Frame\Usharp\src\Usharp.Bundle\Usharp.Bundle.csproj）"
    exit 2
}

# ConfigRoot 归一为绝对路径。
$configAbs = if ([System.IO.Path]::IsPathRooted($ConfigRoot)) { $ConfigRoot } else { Join-Path $root $ConfigRoot }

$fileName = switch ($Kind) {
    'manifest'  { 'declared-manifest.json' }
    'roundspec' { 'round-spec.generated.json' }
    'schema'    { 'reference-schema.generated.json' }
}

if ($OutDir) {
    $outAbs = if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir }
    & dotnet $toolDll $configAbs $outAbs
    exit $LASTEXITCODE
}

if ($OutFile) {
    $tempDir = Join-Path ([IO.Path]::GetTempPath()) ('declared-bundle-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    try {
        & dotnet $toolDll $configAbs $tempDir | Out-Null
        $rc = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
        if ($rc -ne 0) { exit $rc }
        Copy-Item -LiteralPath (Join-Path $tempDir $fileName) -Destination $OutFile -Force
    } finally {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    exit 0
}

Write-Error '须指定 -OutDir 或 -OutFile'
exit 2
