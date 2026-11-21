$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$outputDir = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$report = Join-Path $outputDir 'self-test.txt'
$executable = Join-Path $PSScriptRoot 'dist\CoreLane.v0.13.16.exe'
$process = Start-Process -FilePath $executable -ArgumentList @('--self-test', ('"' + $report + '"')) -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(60000)) { throw 'Self-test timed out. See Task Manager for the dedicated test worker.' }
Get-Content -LiteralPath $report -Encoding UTF8
if ($process.ExitCode -ne 0) { throw ('Self-test failed: ' + $report) }
Write-Host ('Report: ' + $report) -ForegroundColor Green
