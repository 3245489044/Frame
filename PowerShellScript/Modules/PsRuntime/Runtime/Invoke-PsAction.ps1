# Invoke-PsAction.ps1 — ★ LLM 单一入口（动词优先）
# 动词映射表（Ps1 verb.object ↔ C# ParsedCommand.Verb/Target）：
#   read.file      ↔ Verb=read, Target=file,    C# do read file
#   write.file     ↔ Verb=write, Target=file,   C# do write file
#   resolve.path   ↔ Verb=resolve, Target=path, C# do resolve path
# dispatch 三段式（复用 C# ExecuteCommandStage）：
#   精确命中(handler) → gate 门控(workflow) → extension 兜底(non-event)
function Invoke-PsAction {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Action,
        [Parameter(Mandatory)][AllowNull()][hashtable]$Params,
        [array]$Contracts,
        [hashtable]$ExtensionSpec,
        [string]$DefaultObject,
        [string]$OutDir, [string]$RegistryPath, [string]$UsagePath,
        [int]$PromotionThreshold = 3, [int]$PromotionWindowDays = 30
    )
    # 0.归一化（对应 C# CommandParser.Normalize）：处理 LLM 简写/别名
    $normalized = if ($DefaultObject) { Normalize-PsAction -Action $Action -DefaultObject $DefaultObject }
                  else { Normalize-PsAction -Action $Action }

    # 归一化后须为完整 verb.object；纯 verb 无默认 object 视为动作 ID 不完整
    if (-not $normalized -or $normalized -notmatch '\.') {
        return New-PsResult -Status failure -Message "no capability for action: $normalized" -Errors @("no_capability")
    }

    # 1.解析动作 → 命中契约（对应 C# ExecuteCommandStage 精确 handler 命中）
    # Contracts 为 null 时 Resolve-PsCapability 返回 null，不抛异常
    $contract = Resolve-PsCapability -Contracts $Contracts -Action $normalized
    if ($contract) {
        # 2.CanHandler 门控 → 命中执行
        $gate = Test-PsCanHandle -Contract $contract -Arguments $Params
        if ($gate.CanHandle) {
            return Invoke-PsContract -Contract $contract -Arguments $Params
        }
        return New-PsResult -Status failure -Message "cannot handle: $($gate.Reason)" -Errors @($gate.Reason)
    }

    # 3.未命中：有 Spec → 拓展协议；无 Spec → 报缺能力
    if ($ExtensionSpec) {
        return Invoke-PsExtension -Spec $ExtensionSpec -Arguments $Params -OutDir $OutDir -RegistryPath $RegistryPath -UsagePath $UsagePath -PromotionThreshold $PromotionThreshold -PromotionWindowDays $PromotionWindowDays
    }
    return New-PsResult -Status failure -Message "no capability for action: $normalized" -Errors @("no_capability")
}
