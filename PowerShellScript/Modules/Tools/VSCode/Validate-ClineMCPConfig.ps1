<#
.SYNOPSIS
    验证 CLine MCP 配置文件 JSON 格式是否有效
.DESCRIPTION
    快速检查 cline_mcp_settings.json 的 JSON 格式是否正确，
    并列出所有已配置的 MCP 服务器。
    如果格式无效，会提示具体错误位置。
.EXAMPLE
    .\Script\Tools\Validate-ClineMCPConfig.ps1
#>

$configPath = "$env:USERPROFILE\AppData\Roaming\Code\User\globalStorage\saoudrizwan.claude-dev\settings\cline_mcp_settings.json"

if (-not (Test-Path $configPath)) {
    Write-Host "❌ 配置文件不存在: $configPath" -ForegroundColor Red
    exit 1
}

try {
    $content = Get-Content $configPath -Raw
    $config = $content | ConvertFrom-Json
    
    Write-Host "`n✅ JSON 格式验证通过！" -ForegroundColor Green
    Write-Host "📄 配置文件: $configPath" -ForegroundColor Gray
    
    $serverCount = ($config.mcpServers.PSObject.Properties).Count
    Write-Host "🔌 MCP 服务器数量: $serverCount" -ForegroundColor Cyan
    
    Write-Host "`n服务器列表:" -ForegroundColor White
    foreach ($prop in $config.mcpServers.PSObject.Properties) {
        $name = $prop.Name
        $server = $prop.Value
        $disabled = if ($server.disabled) { " [已禁用]" } else { "" }
        $aaCount = ($server.autoApprove).Count
        Write-Host "  ✅ $name$disabled" -ForegroundColor Green
        Write-Host "     autoApprove: $aaCount 个工具" -ForegroundColor Gray
    }
    
    Write-Host "`n📋 所有 MCP 服务器配置正常，CLine 可正常使用。" -ForegroundColor Green
}
catch {
    Write-Host "`n❌ JSON 格式错误！" -ForegroundColor Red
    Write-Host "错误信息: $($_.Exception.Message)" -ForegroundColor Red
    
    # 尝试定位错误位置
    try {
        $content = Get-Content $configPath -Raw
        $lines = $content -split "`r`n|`n"
        Write-Host "`n文件共 $($lines.Count) 行" -ForegroundColor Yellow
        
        # 使用 node.js 获取更详细的错误信息
        $nodeScript = @"
const fs = require('fs');
try {
    const content = fs.readFileSync('$($configPath -replace "'", "''")', 'utf8');
    JSON.parse(content);
} catch(e) {
    console.log('Node.js 解析: ' + e.message);
    const match = e.message.match(/position (\d+)/);
    if (match) {
        const pos = parseInt(match[1]);
        const start = Math.max(0, pos - 40);
        const end = Math.min(content.length, pos + 40);
        console.log('错误附近内容: ' + JSON.stringify(content.substring(start, end)));
        
        // 定位行号
        const before = content.substring(0, pos);
        const lineNum = (before.match(/\n/g) || []).length + 1;
        console.log('错误行号: 第 ' + lineNum + ' 行');
    }
}
"@
        node -e $nodeScript
    }
    catch {
        Write-Host "无法获取详细错误位置" -ForegroundColor Yellow
    }
    
    Write-Host "`n💡 修复建议:" -ForegroundColor Yellow
    Write-Host "  1. 备份文件后，用 VS Code 打开并检查 JSON 语法高亮" -ForegroundColor Gray
    Write-Host "  2. 运行修复脚本: .\Script\Tools\Repair-ClineMCPConfig.ps1" -ForegroundColor Gray
    exit 1
}
