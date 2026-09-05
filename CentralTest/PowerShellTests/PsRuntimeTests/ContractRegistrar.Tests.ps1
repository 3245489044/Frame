# 纯边界测试驱动：验证注册入库的幂等性与边缘兜底
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Runtime\OutputSchema.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Extension\ContractRegistrar.psm1") -Force -ErrorAction Stop
}

Describe "ContractRegistrar 边界" {
    Context "正向注册" {
        It "写入函数到注册表" {
            $reg = Join-Path $env:TEMP "reg_$(Get-Random).json"
            @{ Functions = @{} } | ConvertTo-Json -Depth 5 | Set-Content $reg -Encoding UTF8
            try {
                $c = @{ identity = @{ action = "x.y"; function = "Do-X"; path = "X.psm1"; object = "y"; purpose = "p" } }
                (Register-PsContract -Contract $c -RegistryPath $reg).status | Should -Be "success"
                $obj = Get-Content $reg -Raw -Encoding UTF8 | ConvertFrom-Json
                $obj.Functions.'Do-X'.Path | Should -Be "X.psm1"
                $obj.Functions.'Do-X'.Alias | Should -Be "x.y"
            } finally { Remove-Item $reg -Force -ErrorAction SilentlyContinue }
        }

        It "重复注册幂等（同函数只保留一条）" {
            $reg = Join-Path $env:TEMP "reg2_$(Get-Random).json"
            @{ Functions = @{} } | ConvertTo-Json -Depth 5 | Set-Content $reg -Encoding UTF8
            try {
                $c = @{ identity = @{ action = "x.y"; function = "Do-X"; path = "X.psm1"; object = "y"; purpose = "p" } }
                Register-PsContract -Contract $c -RegistryPath $reg | Out-Null
                Register-PsContract -Contract $c -RegistryPath $reg | Out-Null
                $obj = Get-Content $reg -Raw -Encoding UTF8 | ConvertFrom-Json
                @($obj.Functions.PSObject.Properties | Where-Object Name -eq 'Do-X').Count | Should -Be 1
            } finally { Remove-Item $reg -Force -ErrorAction SilentlyContinue }
        }
    }

    Context "边界: 输入异常" {
        It "contract 为 null 时返回 failure" {
            $reg = Join-Path $env:TEMP "reg_null_$(Get-Random).json"
            try {
                $r = Register-PsContract -Contract $null -RegistryPath $reg
                $r.status | Should -Be "failure"
                $r.errors | Should -Not -BeNullOrEmpty
            } finally { Remove-Item $reg -Force -ErrorAction SilentlyContinue }
        }

        It "contract.identity 缺失时返回 failure" {
            $reg = Join-Path $env:TEMP "reg_noid_$(Get-Random).json"
            @{ Functions = @{} } | ConvertTo-Json -Depth 5 | Set-Content $reg -Encoding UTF8
            try {
                $r = Register-PsContract -Contract @{ other = "x" } -RegistryPath $reg
                $r.status | Should -Be "failure"
            } finally { Remove-Item $reg -Force -ErrorAction SilentlyContinue }
        }

        It "identity.function 缺失时返回 failure" {
            $reg = Join-Path $env:TEMP "reg_nofn_$(Get-Random).json"
            @{ Functions = @{} } | ConvertTo-Json -Depth 5 | Set-Content $reg -Encoding UTF8
            try {
                $c = @{ identity = @{ action = "x.y"; object = "y"; purpose = "p" } }
                $r = Register-PsContract -Contract $c -RegistryPath $reg
                $r.status | Should -Be "failure"
            } finally { Remove-Item $reg -Force -ErrorAction SilentlyContinue }
        }
    }

    Context "边界: 注册表文件" {
        It "注册表文件不存在时自动创建并注册成功" {
            $reg = Join-Path $env:TEMP "reg_absent_$(Get-Random).json"
            try {
                $c = @{ identity = @{ action = "a.b"; function = "Do-A"; path = "A.psm1"; object = "b"; purpose = "p" } }
                $r = Register-PsContract -Contract $c -RegistryPath $reg
                $r.status | Should -Be "success"
                Test-Path $reg | Should -Be $true
                $obj = Get-Content $reg -Raw -Encoding UTF8 | ConvertFrom-Json
                $obj.Functions.'Do-A'.Alias | Should -Be "a.b"
            } finally { Remove-Item $reg -Force -ErrorAction SilentlyContinue }
        }

        It "注册表 JSON 畸形时返回 failure" {
            $reg = Join-Path $env:TEMP "reg_bad_$(Get-Random).json"
            "{ malformed json" | Set-Content $reg -Encoding UTF8
            try {
                $c = @{ identity = @{ action = "a.b"; function = "Do-A"; path = "A.psm1"; object = "b"; purpose = "p" } }
                $r = Register-PsContract -Contract $c -RegistryPath $reg
                $r.status | Should -Be "failure"
            } finally { Remove-Item $reg -Force -ErrorAction SilentlyContinue }
        }
    }
}
