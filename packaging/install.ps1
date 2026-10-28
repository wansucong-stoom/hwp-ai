param([switch]$Install, [string]$AddonRoot = '', [string]$NodePath = '')
$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $PSScriptRoot 'payload'
if (-not $NodePath) { $NodePath = (Get-Command node.exe -ErrorAction Stop).Source }
if (-not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { throw 'Node.js not found. Install Node.js and restart the installer.' }
$nodeVersion = & $NodePath --version
if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v(\d+)\.' -or [int]$Matches[1] -lt 18) { throw 'Node.js 18 or newer is required.' }
if ($Install -and (Get-Process -Name Hwp -ErrorAction SilentlyContinue)) { throw 'Save your documents and close all HWP windows before installing.' }
if (-not $AddonRoot) { $AddonRoot = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'HNC\User\HAddons' }
$addonRoot = $AddonRoot
$destination = Join-Path $addonRoot 'STOOMCodex.Local.0.6.28'
$expectedId = 'D4B89769-4800-4A36-B367-3B8A6E9F3482'
$resolvedRoot = [IO.Path]::GetFullPath($addonRoot) + [IO.Path]::DirectorySeparatorChar
function Assert-AddonPath([string]$candidate) {
    if (-not ([IO.Path]::GetFullPath($candidate)).StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected addon path.' }
}
Assert-AddonPath $destination
if (Test-Path -LiteralPath $destination) { throw "Destination already exists; inspect it before updating: $destination" }
$newAssembly = Join-Path $sourceRoot 'Stoom.Hwp.Codex.Context.dll'
if (-not (Test-Path -LiteralPath $newAssembly -PathType Leaf)) { throw 'Build the context editor first.' }
[xml]$newManifest = Get-Content -LiteralPath (Join-Path $sourceRoot 'manifest.xml') -Raw
if ($newManifest.HncAddonInfo.Id.value -ne $expectedId -or $newManifest.HncAddonInfo.Type.EntryAssembly.value -ne 'Stoom.Hwp.Codex.Context.dll') { throw 'Unexpected new manifest.' }
$previous = @(
    @{ Directory='STOOMCodex.Local.0.1.0'; Assembly='Stoom.Hwp.Codex.dll'; Version='0.1.0' },
    @{ Directory='STOOMCodex.Local.0.2.0'; Assembly='Stoom.Hwp.Codex.Direct.dll'; Version='0.2.0' },
    @{ Directory='STOOMCodex.Local.0.3.0'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.3.0' },
    @{ Directory='STOOMCodex.Local.0.3.1'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.3.1' },
    @{ Directory='STOOMCodex.Local.0.4.0'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.4.0' },
    @{ Directory='STOOMCodex.Local.0.5.0'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.5.0' },
    @{ Directory='STOOMCodex.Local.0.5.1'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.5.1' },
    @{ Directory='STOOMCodex.Local.0.5.2'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.5.2' },
    @{ Directory='STOOMCodex.Local.0.6.0'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.0' },
    @{ Directory='STOOMCodex.Local.0.6.1'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.1' },
    @{ Directory='STOOMCodex.Local.0.6.2'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.2' },
    @{ Directory='STOOMCodex.Local.0.6.3'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.3' },
    @{ Directory='STOOMCodex.Local.0.6.4'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.4' },
    @{ Directory='STOOMCodex.Local.0.6.5'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.5' },
    @{ Directory='STOOMCodex.Local.0.6.6'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.6' },
    @{ Directory='STOOMCodex.Local.0.6.7'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.7' },
    @{ Directory='STOOMCodex.Local.0.6.8'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.8' },
    @{ Directory='STOOMCodex.Local.0.6.9'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.9' },
    @{ Directory='STOOMCodex.Local.0.6.10'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.10' },
    @{ Directory='STOOMCodex.Local.0.6.11'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.11' },
    @{ Directory='STOOMCodex.Local.0.6.12'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.12' },
    @{ Directory='STOOMCodex.Local.0.6.13'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.13' },
    @{ Directory='STOOMCodex.Local.0.6.14'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.14' },
    @{ Directory='STOOMCodex.Local.0.6.15'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.15' },
    @{ Directory='STOOMCodex.Local.0.6.16'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.16' },
    @{ Directory='STOOMCodex.Local.0.6.17'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.17' },
    @{ Directory='STOOMCodex.Local.0.6.18'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.18' },
    @{ Directory='STOOMCodex.Local.0.6.19'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.19' },
    @{ Directory='STOOMCodex.Local.0.6.20'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.20' },
    @{ Directory='STOOMCodex.Local.0.6.21'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.21' },
    @{ Directory='STOOMCodex.Local.0.6.22'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.22' },
    @{ Directory='STOOMCodex.Local.0.6.23'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.23' },
    @{ Directory='STOOMCodex.Local.0.6.24'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.24' },
    @{ Directory='STOOMCodex.Local.0.6.25'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.25' },
    @{ Directory='STOOMCodex.Local.0.6.26'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.26' },
    @{ Directory='STOOMCodex.Local.0.6.27'; Assembly='Stoom.Hwp.Codex.Context.dll'; Version='0.6.27' }
)
$backups = @(foreach ($entry in $previous) {
    $manifestPath = Join-Path (Join-Path $addonRoot $entry.Directory) 'manifest.xml'
    $backupPath = Join-Path (Split-Path $manifestPath) ('manifest.v' + $entry.Version + '.backup')
    Assert-AddonPath $manifestPath
    Assert-AddonPath $backupPath
    if (Test-Path -LiteralPath $manifestPath) {
        [xml]$oldXml = Get-Content -LiteralPath $manifestPath -Raw
        if ($oldXml.HncAddonInfo.Id.value -ne $expectedId -or $oldXml.HncAddonInfo.Type.EntryAssembly.value -ne $entry.Assembly) { throw 'Previous manifest is not owned by this integration.' }
        if (Test-Path -LiteralPath $backupPath) { throw 'Previous manifest backup already exists.' }
        @{ Source=$manifestPath; Backup=$backupPath }
    }
})
$files = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse)
$plan = [ordered]@{ source=$sourceRoot; destination=$destination; files=@($files | ForEach-Object { $_.FullName.Substring($sourceRoot.Length+1) }); previousManifestBackups=$backups; developerModeChanged=$false; applicationRestarted=$false }
if (-not $Install) { $plan | ConvertTo-Json -Depth 5; return }
# Keep the new manifest absent until the complete new folder and config are ready.
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($file in $files) {
    $relative = $file.FullName.Substring($sourceRoot.Length+1)
    if ($relative -eq 'manifest.xml') { continue }
    $targetPath = Join-Path $destination $relative
    Assert-AddonPath $targetPath
    New-Item -ItemType Directory -Path (Split-Path $targetPath) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $targetPath
}
$configPath = Join-Path $destination 'hwp-codex.json'
$config = [ordered]@{ NodePath = [IO.Path]::GetFullPath($NodePath); HostPath = (Join-Path $destination 'bridge\editor-host.mjs'); StoomModulePath = (Join-Path $destination 'bridge\vendor\stoom-codex-native-host.mjs') }
$config | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding utf8
$moved = @()
try {
    foreach ($backup in $backups) { Move-Item -LiteralPath $backup.Source -Destination $backup.Backup; $moved += $backup }
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'manifest.xml') -Destination $destination
} catch {
    foreach ($backup in $moved) { if (-not (Test-Path -LiteralPath $backup.Source)) { Move-Item -LiteralPath $backup.Backup -Destination $backup.Source } }
    throw
}
$plan['installed']=$true
$plan | ConvertTo-Json -Depth 5
