# Invoke-PsExtension.ps1 — 拓展协议编排：脚手架生成 → 注册 → 用量记录 → 返回 scaffolded
# 调用方已判定 CanHandle=false，本入口闭合拓展逻辑（不在 PS1 内调 LLM）

function Invoke-PsExtension {
    param(
        [hashtable]$Spec, [hashtable]$Arguments,
        [string]$OutDir, [string]$RegistryPath, [string]$UsagePath,
        [int]$PromotionThreshold = 3, [int]$PromotionWindowDays = 30
    )
    try {
        # 边界: Spec 为 null
        if (-not $Spec) { return New-PsResult -Status failure -Message "spec null" -Errors @("spec_null") }

        # 1. 生成脚手架（OutDir 不存在或 Spec 缺 action/function 时 New-PsContractScript 返回 failure）
        $scriptPath = Join-Path $OutDir "$($Spec.function).psm1"
        $gen = New-PsContractScript -Spec $Spec -OutputPath $scriptPath
        if ($gen.status -ne "success") { return $gen }

        # 2. 注册
        $contract = @{
            identity = @{
                action = $Spec.action; verb = $Spec.verb; object = $Spec.object
                purpose = $Spec.purpose; function = $Spec.function; path = $scriptPath
            }
            params = $Spec.params
            env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() }
        }
        $reg = Register-PsContract -Contract $contract -RegistryPath $RegistryPath
        if ($reg.status -ne "success") { return $reg }

        # 3. 执行：脚手架未填实现，返回 scaffolded 等待 SK 填充后重试
        # 4. 升级：记录用量
        $null = Update-PsContractUsage -Action $Spec.action -StorePath $UsagePath
        $promo = Get-PsContractPromotion -Action $Spec.action -StorePath $UsagePath -Threshold $PromotionThreshold -WindowDays $PromotionWindowDays

        return New-PsResult -Status success -Message "extension scaffolded, awaiting implementation" -Data @{
            phase = "scaffolded"; action = $Spec.action; function = $Spec.function
            path = $scriptPath; promotion = $promo
        }
    } catch {
        return New-PsResult -Status failure -Message "extension error" -Errors @($_.Exception.Message)
    }
}
