$ErrorActionPreference='Stop'
$Root='D:\Project\Program\WorkFlowPrj'
$csFiles = Get-ChildItem -Path $Root -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\node_modules\\|\\obj\\|\\bin\\|\\_deprecated\\|\\backups\\|\\BuildOutput\\' }
$referenced=[System.Collections.Generic.HashSet[string]]::new()
foreach($f in $csFiles){ $t=(Get-Content -LiteralPath $f.FullName -Raw -ErrorAction SilentlyContinue); if($null -eq $t){$t=''}
  foreach($m in [regex]::Matches($t,'\.Log\(\s*"([A-Za-z0-9_]+)"')){[void]$referenced.Add($m.Groups[1].Value)}
  foreach($m in [regex]::Matches($t,'ConfigFileLoaderLog\(\s*"([A-Za-z0-9_]+)"')){[void]$referenced.Add($m.Groups[1].Value)}
}
$parseFail=0; $orphan=0; $files=0; $registeredTotal=0
$jf=Get-ChildItem -Path $Root -Recurse -Filter '*_log_messages.json' -File | Where-Object { $_.FullName -notmatch '\\node_modules\\|\\obj\\|\\bin\\|\\_deprecated\\|\\backups\\|\\BuildOutput\\' }
foreach($f in $jf){
  try{$j=Get-Content -LiteralPath $f.FullName -Raw|ConvertFrom-Json}catch{ write-host "PARSE-FAIL: $($f.Name) :: $($_.Exception.Message)";$parseFail++;continue }
  if($null -eq $j -or $null -eq $j.messages){ write-host "NO-MESSAGES: $($f.Name)";$parseFail++;continue }
  $files++
  $names=@($j.messages.PSObject.Properties.Name|Where-Object{$_})
  $registeredTotal += $names.Count
  $orph=@($names|Where-Object{-not $referenced.Contains($_)})
  if($orph.Count -gt 0){ write-host "ORPHAN($($orph.Count)): $($f.Name) : $($orph -join ',')"; $orphan += $orph.Count }
}
write-host ("VERIFY: files={0} registered={1} parseFail={2} orphan(msgId无调用方)={3}" -f $files,$registeredTotal,$parseFail,$orphan)