# verify-secrets.ps1 - zero-exposure verification after DPAPI migration.
# Decrypts dpapi: values IN MEMORY (same-user DPAPI), then scans target files
# for the plaintext values. Prints ONLY CLEAN/LEAK - never the values.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Security

$conf = "E:\translator\scripts\config.conf"
$log = Join-Path $env:TEMP "tfd_host_err.log"
$repo = "E:\translator\release\translate-for-developers"

$lines = Get-Content $conf -Encoding UTF8
$values = @{}
$dpapiCount = 0
$plainCount = 0
foreach ($line in $lines) {
    if ($line -match "^baidu_(appid|secret)\s*=\s*(.+)$") {
        $k = $Matches[1]; $v = $Matches[2].Trim()
        if ($v.StartsWith("dpapi:")) {
            $dpapiCount++
            try {
                $bytes = [Convert]::FromBase64String($v.Substring(6))
                $plain = [Text.Encoding]::UTF8.GetString(
                    [Security.Cryptography.ProtectedData]::Unprotect($bytes, $null, "CurrentUser"))
                $values[$k] = $plain
            } catch { Write-Host ("FAIL[" + $k + "] dpapi value undecryptable on this machine") }
        } else {
            if ($v.Length -gt 0) { Write-Host ("LEAK[" + $k + "] value on disk is PLAINTEXT (migration did not run)") }
            $values[$k] = $v
        }
    }
    if ($line -match "^(deepl_key|llm_api_key)\s*=\s*(.+)$") {
        $k = $Matches[1]; $v = $Matches[2].Trim()
        if ($v.StartsWith("dpapi:")) {
            $dpapiCount++
            try {
                $bytes = [Convert]::FromBase64String($v.Substring(6))
                $plain = [Text.Encoding]::UTF8.GetString(
                    [Security.Cryptography.ProtectedData]::Unprotect($bytes, $null, "CurrentUser"))
                $values[$k] = $plain
            } catch { Write-Host ("FAIL[" + $k + "] dpapi value undecryptable on this machine") }
        } else {
            if ($v.Length -gt 0) { $plainCount++; Write-Host ("LEAK[" + $k + "] value on disk is PLAINTEXT") }
            if ($v.Length -gt 0) { $values[$k] = $v }
        }
    }
}
Write-Host ("dpapi-lines=" + $dpapiCount + " plain-secret-lines=" + $plainCount)

foreach ($k in $values.Keys) {
    $v = $values[$k]
    if ([IO.File]::ReadAllText($conf).Contains($v)) {
        Write-Host ("LEAK[" + $k + "] plaintext in config.conf") } else {
        Write-Host ("CLEAN[" + $k + "] not in config.conf") }
    if ([IO.File]::ReadAllText($log).Contains($v)) {
        Write-Host ("LEAK[" + $k + "] plaintext in host log") } else {
        Write-Host ("CLEAN[" + $k + "] not in host log") }
    $hit = $false
    Get-ChildItem $repo -Recurse -File | Where-Object { $_.FullName -notmatch "\\\.git\\" } | ForEach-Object {
        if (-not $hit) {
            try { if ([IO.File]::ReadAllText($_.FullName).Contains($v)) {
                $hit = $true; Write-Host ("LEAK[" + $k + "] in repo: " + $_.FullName) } } catch {}
        }
    }
    if (-not $hit) { Write-Host ("CLEAN[" + $k + "] not in repo tree") }
}
Write-Host "SECRET-VERIFY-DONE"
