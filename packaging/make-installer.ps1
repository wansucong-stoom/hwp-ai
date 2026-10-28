param([string]$OutputPath = (Join-Path $PSScriptRoot '..\downloads\STOOM-AI-0.6.28.zip'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$payload = Join-Path $root 'dist\StoomCodexContext'
if (-not (Test-Path (Join-Path $payload 'Stoom.Hwp.Codex.Context.dll'))) { throw 'Build the add-on before packaging.' }
$stage = Join-Path $root 'dist\installer-stage'
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $payload -Destination (Join-Path $stage 'payload') -Recurse
Remove-Item -LiteralPath (Join-Path $stage 'payload\hwp-codex.json') -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $stage 'payload\bridge\vendor\provenance.json') -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.cmd') -Destination $stage
New-Item -ItemType Directory -Path (Split-Path $OutputPath) -Force | Out-Null
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $OutputPath -Force
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
