# Builds Battery into .\dist as two self-contained single-file executables:
#   dist\Battery.exe             the strip (run this)
#   dist\Battery.StatusLine.exe  the Claude Code status-line shim; it must stay beside Battery.exe
# -Version overrides the version in Directory.Build.props (the release workflow passes the tag's).
param([string]$Version)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none',
            '-p:ContinuousIntegrationBuild=true', '-o', 'dist')
if ($Version) { $common += "-p:Version=$Version" }

if (Test-Path dist) { Remove-Item -Recurse -Force dist }

dotnet publish src/Battery.App @common
if ($LASTEXITCODE -ne 0) { throw "Publishing Battery failed." }

dotnet publish src/Battery.StatusLine @common
if ($LASTEXITCODE -ne 0) { throw "Publishing Battery.StatusLine failed." }

Get-ChildItem dist | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
