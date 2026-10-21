param([string]$OutputName = 'CoreAllocator.v0.1.1.exe')
$ErrorActionPreference = 'Stop'
if ([System.IO.Path]::GetFileName($OutputName) -ne $OutputName -or -not $OutputName.EndsWith('.exe')) { throw 'OutputName must be an executable filename.' }
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Cannot find the Windows .NET Framework C# compiler. This project needs Windows 11 x64 and .NET Framework 4.8.' }
$outputDir = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName })
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/warn:4', '/warnaserror+', '/codepage:65001', ('/out:' + (Join-Path $outputDir $OutputName)), ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Runtime.Serialization.dll', '/reference:System.Xml.dll')
& $compiler @arguments @sourceFiles
if ($LASTEXITCODE -ne 0) { throw ('Build failed, compiler exit code: ' + $LASTEXITCODE) }
Copy-Item -LiteralPath (Join-Path $projectRoot 'App.config') -Destination (Join-Path $outputDir ($OutputName + '.config')) -Force
Write-Host ('Built: ' + (Join-Path $outputDir $OutputName)) -ForegroundColor Green
