<#
.SYNOPSIS
    通用文件下载模块 — 支持断点续传、缓存、重试、哈希校验

.DESCRIPTION
    提供可靠的文件下载功能，核心特性：
    - 断点续传：下载中断后只续传未完成部分，不重新下载整个文件
    - 缓存管理：已下载完成且哈希匹配的文件直接复用
    - 自动重试：网络错误时指数退避重试
    - 哈希校验：下载完成后可选 SHA256/MD5 校验
    - 进度显示：支持实时下载进度条

    包含函数:
        Invoke-FileDownload     — 通用文件下载（核心）
        Get-FileHashValue       — 计算文件哈希值
        Clear-DownloadCache     — 清理下载缓存文件

    依赖:
        BasicMethod-Output.psm1（提供 Write-Result 等输出函数）

    用法:
        Import-Module .\BasicMethod-Download.psm1
        Invoke-FileDownload -Url "https://..." -Destination "path/to/file"
        Invoke-FileDownload -Url "https://..." -Destination "path/to/file" -Hash "sha256:abc..." -Progress

.NOTES
    模块名: BasicMethod-Download
    版本: 1.0.0
#>

# ============================================================
# 模块初始化
# ============================================================

# 尝试加载输出模块（非强制）
$script:OutputModuleLoaded = $false
try {
    $moduleDir = Split-Path -Parent $PSScriptRoot
    $outputModPath = Join-Path $moduleDir "BasicMethod-Output.psm1"
    if (Test-Path $outputModPath) {
        Import-Module $outputModPath -Force -DisableNameChecking -ErrorAction SilentlyContinue
        $script:OutputModuleLoaded = $true
    }
} catch { }

# ============================================================
# 内部辅助函数
# ============================================================

function _Write-Info {
    param([string]$Message)
    if ($script:OutputModuleLoaded) { Write-Result "INFO" $Message "Gray" }
    else { Write-Host "  ℹ️  $Message" -ForegroundColor Gray }
}

function _Write-Ok {
    param([string]$Message)
    if ($script:OutputModuleLoaded) { Write-Result "OK" $Message "Green" }
    else { Write-Host "  ✅ $Message" -ForegroundColor Green }
}

function _Write-Warn {
    param([string]$Message)
    if ($script:OutputModuleLoaded) { Write-Result "WARN" $Message "Yellow" }
    else { Write-Host "  ⚠️  $Message" -ForegroundColor Yellow }
}

function _Write-Error {
    param([string]$Message)
    if ($script:OutputModuleLoaded) { Write-Result "ERROR" $Message "Red" }
    else { Write-Host "  ❌ $Message" -ForegroundColor Red }
}

function _Format-FileSize {
    <#
    .SYNOPSIS
        格式化文件大小为人类可读格式
    #>
    param([long]$Bytes)

    if ($Bytes -ge 1TB) { return "{0:N2} TB" -f ($Bytes / 1TB) }
    if ($Bytes -ge 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N2} MB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N2} KB" -f ($Bytes / 1KB) }
    return "$Bytes B"
}

# ============================================================
# 核心函数
# ============================================================

<#
.SYNOPSIS
    计算文件的哈希值
.PARAMETER Path
    文件路径
.PARAMETER Algorithm
    哈希算法（SHA256 或 MD5，默认 SHA256）
.OUTPUTS
    [string] 哈希值的十六进制字符串，失败返回 $null
.EXAMPLE
    Get-FileHashValue -Path "file.zip"
    Get-FileHashValue -Path "file.zip" -Algorithm MD5
#>
function Get-FileHashValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateScript({ Test-Path $_ })]
        [string]$Path,

        [ValidateSet("SHA256", "MD5")]
        [string]$Algorithm = "SHA256"
    )

    try {
        $hash = Get-FileHash -Path $Path -Algorithm $Algorithm -ErrorAction Stop
        return $hash.Hash.ToLower()
    } catch {
        _Write-Error "计算哈希失败: $_"
        return $null
    }
}

<#
.SYNOPSIS
    通用文件下载 — 支持断点续传、缓存、重试、哈希校验
.DESCRIPTION
    核心下载函数，特性：
    - 断点续传：通过 HTTP Range 请求只下载未完成部分
    - 缓存复用：已下载且哈希匹配的文件直接返回
    - 自动重试：网络错误时指数退避重试（仅对可重试错误）
    - 哈希校验：下载完成后验证文件完整性
    - 进度显示：通过 -Progress 参数启用实时进度条

.PARAMETER Url
    下载 URL（必填）
.PARAMETER Destination
    目标文件保存路径（必填）
.PARAMETER Hash
    期望的哈希值，格式 "sha256:abc123..." 或 "md5:def456..."
    提供后下载完成会自动校验
.PARAMETER HashAlgorithm
    哈希算法（SHA256 或 MD5，默认 SHA256）
    仅在未提供 Hash 参数时用于缓存标识
.PARAMETER MaxRetries
    最大重试次数（默认 5）
.PARAMETER RetryDelay
    初始重试延迟秒数（默认 5，指数退避：5→10→20→40→80）
.PARAMETER Timeout
    单次 HTTP 请求超时秒数（默认 300）
.PARAMETER Force
    强制重新下载（忽略已有缓存和断点文件）
.PARAMETER Progress
    显示下载进度条
.PARAMETER WhatIf
    模拟模式，仅预览不执行

.OUTPUTS
    [bool] 下载成功返回 $true，失败返回 $false

.EXAMPLE
    Invoke-FileDownload -Url "https://example.com/file.zip" -Destination "downloads/file.zip"
    Invoke-FileDownload -Url "https://..." -Destination "file.zip" -Hash "sha256:abc..." -Progress
    Invoke-FileDownload -Url "https://..." -Destination "file.zip" -MaxRetries 10 -Force
#>
function Invoke-FileDownload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Destination,

        [string]$Hash = "",

        [ValidateSet("SHA256", "MD5")]
        [string]$HashAlgorithm = "SHA256",

        [ValidateRange(0, 100)]
        [int]$MaxRetries = 5,

        [ValidateRange(1, 300)]
        [int]$RetryDelay = 5,

        [ValidateRange(10, 3600)]
        [int]$Timeout = 300,

        [switch]$Force,

        [switch]$Progress,

        [switch]$WhatIf
    )

    # ============================================================
    # 解析哈希参数
    # ============================================================
    $expectedHash = $Hash
    $hashAlgo = $HashAlgorithm
    if ($expectedHash -match '^(sha256|md5):(.+)$') {
        $hashAlgo = $matches[1].ToUpper()
        $expectedHash = $matches[2].Trim().ToLower()
    }

    # ============================================================
    # 路径准备
    # ============================================================
    $destDir = Split-Path -Parent $Destination
    $destFile = Split-Path -Leaf $Destination
    $partFile = "$Destination.part"       # 断点续传临时文件
    $hashFile = "$Destination.hash"       # 哈希记录文件

    # 确保目标目录存在
    if (-not (Test-Path $destDir)) {
        if ($WhatIf) {
            _Write-Info "[模拟] 创建目录: $destDir"
        } else {
            $null = New-Item -ItemType Directory -Path $destDir -Force
        }
    }

    # ============================================================
    # WhatIf 模式
    # ============================================================
    if ($WhatIf) {
        _Write-Info "[模拟] 下载: $Url"
        _Write-Info "[模拟] 目标: $Destination"
        if ($expectedHash) { _Write-Info "[模拟] 哈希校验: $hashAlgo=$expectedHash" }
        _Write-Info "[模拟] 重试策略: 最多 $MaxRetries 次, 初始延迟 ${RetryDelay}s"
        return $true
    }

    # ============================================================
    # 步骤 1：检查缓存（已完整下载且哈希匹配）
    # ============================================================
    if (-not $Force -and (Test-Path $Destination)) {
        $fileSize = (Get-Item $Destination).Length
        if ($expectedHash) {
            _Write-Info "文件已存在 ($(_Format-FileSize $fileSize))，验证哈希..."
            $actualHash = Get-FileHashValue -Path $Destination -Algorithm $hashAlgo
            if ($actualHash -eq $expectedHash) {
                _Write-Ok "哈希匹配，使用缓存文件"
                return $true
            } else {
                _Write-Warn "哈希不匹配 (期望: $expectedHash, 实际: $actualHash)，重新下载"
            }
        } else {
            # 检查是否有哈希记录文件
            if (Test-Path $hashFile) {
                $recordedHash = (Get-Content $hashFile -Raw -ErrorAction SilentlyContinue).Trim()
                if ($recordedHash) {
                    $actualHash = Get-FileHashValue -Path $Destination -Algorithm $hashAlgo
                    if ($actualHash -eq $recordedHash) {
                        _Write-Ok "文件已存在且哈希记录匹配 ($(_Format-FileSize $fileSize))"
                        return $true
                    } else {
                        _Write-Warn "文件已变更，重新下载"
                    }
                } else {
                    _Write-Info "文件已存在 ($(_Format-FileSize $fileSize))，无哈希记录，直接使用"
                    return $true
                }
            } else {
                _Write-Info "文件已存在 ($(_Format-FileSize $fileSize))，直接使用"
                return $true
            }
        }
    }

    # ============================================================
    # 步骤 2：检查断点续传文件
    # ============================================================
    $resumeOffset = 0
    if (-not $Force -and (Test-Path $partFile)) {
        $resumeOffset = (Get-Item $partFile).Length
        if ($resumeOffset -gt 0) {
            _Write-Info "发现断点文件，从偏移 $(_Format-FileSize $resumeOffset) 处续传"
        }
    }

    # ============================================================
    # 步骤 3：执行下载（带重试）
    # ============================================================
    _Write-Info "开始下载: $Url"
    _Write-Info "目标文件: $Destination"
    if ($expectedHash) { _Write-Info "哈希校验: $hashAlgo=$expectedHash" }

    $downloadSuccess = $false
    $lastError = $null
    $totalBytes = $null

    for ($attempt = 0; $attempt -le $MaxRetries; $attempt++) {
        if ($attempt -gt 0) {
            $delay = [Math]::Min($RetryDelay * [Math]::Pow(2, $attempt - 1), 120)
            _Write-Warn "第 $attempt 次重试，等待 ${delay}s..."
            Start-Sleep -Seconds $delay

            # 重试时检查断点文件大小
            if (Test-Path $partFile) {
                $resumeOffset = (Get-Item $partFile).Length
                _Write-Info "断点偏移: $(_Format-FileSize $resumeOffset)"
            }
        }

        try {
            $client = New-Object System.Net.Http.HttpClient
            $client.Timeout = [TimeSpan]::FromSeconds($Timeout)

            # 构建请求（支持断点续传）
            $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $Url)
            if ($resumeOffset -gt 0) {
                $request.Headers.Range = New-Object System.Net.Http.Headers.RangeHeaderValue($resumeOffset, $null)
            }

            # 发送请求
            $response = $client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()

            if (-not $response.IsSuccessStatusCode -and $response.StatusCode -ne 206) {
                # 服务器不支持 Range 请求或返回错误
                if ($response.StatusCode -eq 416) {
                    # Range Not Satisfiable — 文件已完整下载
                    _Write-Warn "服务器返回 416（文件可能已完整），尝试直接使用 .part 文件"
                    $downloadSuccess = $true
                    break
                }

                _Write-Error "HTTP $( [int]$response.StatusCode ): $($response.ReasonPhrase)"
                if ([int]$response.StatusCode -ge 400 -and [int]$response.StatusCode -lt 500) {
                    _Write-Error "客户端错误（4xx），不重试"
                    break
                }
                $lastError = "HTTP $( [int]$response.StatusCode )"
                continue
            }

            # 获取内容长度
            $contentLength = $response.Content.Headers.ContentLength
            if ($contentLength -gt 0 -and $resumeOffset -eq 0) {
                $totalBytes = $contentLength
            } elseif ($contentLength -gt 0) {
                $totalBytes = $resumeOffset + $contentLength
            }

            # 打开文件流（续传模式追加，新下载覆盖）
            $fileMode = if ($resumeOffset -gt 0) { [System.IO.FileMode]::Append } else { [System.IO.FileMode]::Create }
            $fileStream = [System.IO.File]::Open($partFile, $fileMode)

            try {
                $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                $buffer = New-Object byte[] 81920  # 80KB 缓冲区
                $bytesRead = 0
                $totalRead = $resumeOffset
                $lastProgressUpdate = [DateTime]::MinValue

                while (($bytesRead = $stream.ReadAsync($buffer, 0, $buffer.Length).GetAwaiter().GetResult()) -gt 0) {
                    $fileStream.Write($buffer, 0, $bytesRead)
                    $totalRead += $bytesRead

                    # 进度显示（最多每秒更新一次）
                    if ($Progress -and $totalBytes -gt 0) {
                        $now = [DateTime]::UtcNow
                        if (($now - $lastProgressUpdate).TotalSeconds -ge 1) {
                            $percent = [Math]::Min(100, [Math]::Round($totalRead / $totalBytes * 100, 1))
                            $downloaded = _Format-FileSize $totalRead
                            $total = _Format-FileSize $totalBytes
                            Write-Progress -Activity "下载中" -Status "$downloaded / $total" -PercentComplete $percent
                            $lastProgressUpdate = $now
                        }
                    }
                }

                if ($Progress) { Write-Progress -Activity "下载中" -Completed }

                # 验证下载完整性
                if ($totalBytes -gt 0 -and $totalRead -lt $totalBytes) {
                    _Write-Warn "下载不完整: $(_Format-FileSize $totalRead) / $(_Format-FileSize $totalBytes)"
                    $lastError = "下载不完整"
                    continue
                }

                $downloadSuccess = $true
                break

            } finally {
                $fileStream.Close()
                $fileStream.Dispose()
                $stream.Dispose()
            }

        } catch {
            $lastError = $_.Exception.Message
            _Write-Warn "下载失败: $lastError"

            # 检查断点文件是否已增长
            if (Test-Path $partFile) {
                $newOffset = (Get-Item $partFile).Length
                if ($newOffset -gt $resumeOffset) {
                    _Write-Info "已下载 $(_Format-FileSize ($newOffset - $resumeOffset)) 新数据，下次续传"
                    $resumeOffset = $newOffset
                }
            }

        } finally {
            if ($client) { $client.Dispose() }
        }
    }

    # ============================================================
    # 步骤 4：处理下载结果
    # ============================================================
    if (-not $downloadSuccess) {
        if ($Progress) { Write-Progress -Activity "下载中" -Completed }
        _Write-Error "下载失败（已重试 $MaxRetries 次）"
        if ($lastError) { _Write-Error "最后错误: $lastError" }
        _Write-Info "断点文件保留在: $partFile"
        _Write-Info "下次运行将自动续传"
        return $false
    }

    # ============================================================
    # 步骤 5：重命名 .part → 目标文件
    # ============================================================
    if (Test-Path $Destination) {
        Remove-Item $Destination -Force -ErrorAction SilentlyContinue
    }
    Rename-Item $partFile $Destination -Force

    $finalSize = (Get-Item $Destination).Length
    _Write-Ok "下载完成: $(_Format-FileSize $finalSize)"

    # ============================================================
    # 步骤 6：哈希校验
    # ============================================================
    $hashVerified = $false
    if ($expectedHash) {
        _Write-Info "验证哈希..."
        $actualHash = Get-FileHashValue -Path $Destination -Algorithm $hashAlgo
        if ($actualHash -eq $expectedHash) {
            _Write-Ok "哈希校验通过"
            $hashVerified = $true
        } else {
            _Write-Error "哈希校验失败"
            _Write-Error "  期望: $expectedHash"
            _Write-Error "  实际: $actualHash"
            Remove-Item $Destination -Force -ErrorAction SilentlyContinue
            return $false
        }
    }

    # ============================================================
    # 步骤 7：写入哈希记录（供后续缓存判断）
    # ============================================================
    if ($hashVerified -or -not $expectedHash) {
        try {
            $recordHash = if ($expectedHash) { $expectedHash } else { Get-FileHashValue -Path $Destination -Algorithm $hashAlgo }
            if ($recordHash) {
                $recordHash | Out-File -FilePath $hashFile -Encoding UTF8 -Force
            }
        } catch { }
    }

    return $true
}

<#
.SYNOPSIS
    清理下载缓存文件（.part 和 .hash 文件）
.DESCRIPTION
    递归清理指定目录下的所有 .part 和 .hash 缓存文件。
    也可清理单个文件的缓存。

.PARAMETER Path
    要清理的目录或文件路径
    目录：递归清理该目录下所有 .part 和 .hash 文件
    文件：清理 {文件}.part 和 {文件}.hash
.PARAMETER All
    清理所有缓存文件（包括已完成的 .hash 记录）
.PARAMETER WhatIf
    模拟模式，仅预览不执行

.EXAMPLE
    Clear-DownloadCache -Path "downloads/"
    Clear-DownloadCache -Path "downloads/file.zip"
    Clear-DownloadCache -Path "downloads/" -All
#>
function Clear-DownloadCache {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [switch]$All,

        [switch]$WhatIf
    )

    $resolvedPath = Resolve-Path $Path -ErrorAction SilentlyContinue
    if (-not $resolvedPath) {
        _Write-Error "路径不存在: $Path"
        return $false
    }

    $removed = 0

    if (Test-Path -Path $resolvedPath -PathType Container) {
        # 目录模式：递归查找
        $patterns = @("*.part")
        if ($All) { $patterns += "*.hash" }

        foreach ($pattern in $patterns) {
            $files = Get-ChildItem -Path $resolvedPath -Filter $pattern -Recurse -ErrorAction SilentlyContinue
            foreach ($file in $files) {
                if ($WhatIf) {
                    _Write-Info "[模拟] 删除: $($file.FullName)"
                } else {
                    Remove-Item $file.FullName -Force -ErrorAction SilentlyContinue
                }
                $removed++
            }
        }
    } else {
        # 文件模式：清理关联的 .part 和 .hash
        $partFile = "$resolvedPath.part"
        $hashFile = "$resolvedPath.hash"

        if (Test-Path $partFile) {
            if ($WhatIf) {
                _Write-Info "[模拟] 删除: $partFile"
            } else {
                Remove-Item $partFile -Force -ErrorAction SilentlyContinue
            }
            $removed++
        }

        if ($All -and (Test-Path $hashFile)) {
            if ($WhatIf) {
                _Write-Info "[模拟] 删除: $hashFile"
            } else {
                Remove-Item $hashFile -Force -ErrorAction SilentlyContinue
            }
            $removed++
        }
    }

    if ($WhatIf) {
        _Write-Info "[模拟] 共 $removed 个缓存文件将被删除"
    } else {
        _Write-Ok "已清理 $removed 个缓存文件"
    }
    return $true
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    "Invoke-FileDownload"
    "Get-FileHashValue"
    "Clear-DownloadCache"
)
