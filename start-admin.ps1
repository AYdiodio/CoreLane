$ErrorActionPreference = 'Stop'
$appPath = Join-Path $PSScriptRoot 'dist\CoreLane.v0.13.17.exe'
if (-not (Test-Path -LiteralPath $appPath)) { throw 'Application is missing. Run Build.cmd first.' }
try {
    Start-Process -FilePath $appPath -WorkingDirectory ([System.IO.Path]::GetDirectoryName($appPath)) -Verb RunAs -WindowStyle Normal | Out-Null
} catch [System.ComponentModel.Win32Exception] {
    if ($_.Exception.NativeErrorCode -eq 1223) { Write-Host 'Administrator launch was cancelled. No changes were made.'; exit 0 }
    throw
}
