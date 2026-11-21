param(
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [Parameter(Mandatory = $true)][string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
$compiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$version = [Reflection.AssemblyName]::GetAssemblyName($executable).Version.ToString(3)
if ([IO.Path]::GetFileName($executable) -ne "CoreLane.v$version.exe") {
    throw 'Choose a versioned CoreLane executable produced by build.ps1.'
}
if (-not (Test-Path -LiteralPath ($executable + '.config') -PathType Leaf)) {
    throw 'The matching CoreLane .exe.config file is missing.'
}

$messages = Join-Path (Split-Path -LiteralPath $compiler) 'Languages\ChineseSimplified.isl'
if (-not (Test-Path -LiteralPath $messages -PathType Leaf)) {
    throw 'Install ChineseSimplified.isl in the Inno Setup Languages directory.'
}

$outputDir = Join-Path $PSScriptRoot "dist\publish-v$version"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$script = Join-Path $PSScriptRoot 'installer\CoreLane.iss'
& $compiler "/DCoreLaneVersion=$version" "/DCoreLaneSource=$executable" "/O$outputDir" $script
if ($LASTEXITCODE -ne 0) { throw ('Installer compilation failed: ' + $LASTEXITCODE) }
