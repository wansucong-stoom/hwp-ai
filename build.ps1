param(
    [string]$HancomBin = 'C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin',
    [string]$StoomModule = (Join-Path $PSScriptRoot 'bridge\vendor\stoom-codex-native-host.mjs'),
    [string]$NodePath = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'dist\StoomCodexContext' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$workspaceRoot = [IO.Path]::GetFullPath($projectRoot) + [IO.Path]::DirectorySeparatorChar
if (-not $outputRoot.StartsWith($workspaceRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build output must stay inside this workspace.'
}
if (-not $StoomModule) { throw 'The STOOM native host module path is required.' }
if (-not $NodePath) { $NodePath = (Get-Command node.exe -ErrorAction Stop).Source }
$cscPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$frameworkRoot = Split-Path $cscPath
$requiredFiles = @(
    $cscPath, $NodePath, $StoomModule,
    (Join-Path $HancomBin 'Hnc.Foundation.Interface.dll'),
    (Join-Path $HancomBin 'HwpAddonHelper.dll'),
    (Join-Path $projectRoot 'bridge\hwp-codex-host.mjs'),
    (Join-Path $projectRoot 'bridge\editor-host.mjs'),
    (Join-Path $projectRoot 'addin\StoomCodex.cs'),
    (Join-Path $projectRoot 'addin\SelectionMenu.cs'),
    (Join-Path $projectRoot 'addin\NativeEditor.cs'),
    (Join-Path $projectRoot 'addin\SelectionEditor.cs'),
    (Join-Path $projectRoot 'addin\FrameRoute.cs'),
    (Join-Path $projectRoot 'addin\CaretAnchor.cs'),
    (Join-Path $projectRoot 'addin\ConversationIntent.cs'),
    (Join-Path $projectRoot 'addin\NativeComposer.cs'),
    (Join-Path $projectRoot 'addin\NativeRichComposer.cs'),
    (Join-Path $projectRoot 'addin\TableStylePlan.cs'),
    (Join-Path $projectRoot 'addin\TableStyleBatchPlanner.cs'),
    (Join-Path $projectRoot 'addin\TableDesignSnapshot.cs'),
    (Join-Path $projectRoot 'addin\TableDesignCompiler.cs'),
    (Join-Path $projectRoot 'addin\TableDesignService.cs'),
    (Join-Path $projectRoot 'addin\TableStableSnapshot.cs'),
    (Join-Path $projectRoot 'addin\NativePageContext.cs'),
    (Join-Path $projectRoot 'addin\ResponsivePageReader.cs'),
    (Join-Path $projectRoot 'addin\NativeResponsivePageSource.cs'),
    (Join-Path $projectRoot 'addin\NativePageIndicator.cs'),
    (Join-Path $projectRoot 'addin\NativePagePositionEvidence.cs'),
    (Join-Path $projectRoot 'addin\NativePageMapping.cs'),
    (Join-Path $projectRoot 'addin\NativeDocumentScan.cs'),
    (Join-Path $projectRoot 'addin\NativeDocumentInspection.cs'),
    (Join-Path $projectRoot 'addin\NativeDocumentUpdateBackend.cs'),
    (Join-Path $projectRoot 'addin\DocumentStructure.cs'),
    (Join-Path $projectRoot 'addin\DocumentTargetDiscovery.cs'),
    (Join-Path $projectRoot 'addin\DocumentTargetDiscoveryService.cs'),
    (Join-Path $projectRoot 'addin\DocumentTargetAnchors.cs'),
    (Join-Path $projectRoot 'addin\OnDemandTargetLocator.cs'),
    (Join-Path $projectRoot 'addin\NativeRepeatFindProbe.cs'),
    (Join-Path $projectRoot 'addin\HwpParagraphProofs.cs'),
    (Join-Path $projectRoot 'addin\AutomaticPageUpdate.cs'),
    (Join-Path $projectRoot 'addin\NativeAutomaticPageUpdateEnvironment.cs'),
    (Join-Path $projectRoot 'addin\TextOnlyAutomaticPageEvidence.cs'),
    (Join-Path $projectRoot 'addin\DocumentAddressMap.cs'),
    (Join-Path $projectRoot 'addin\DocumentUpdateTransaction.cs'),
    (Join-Path $projectRoot 'addin\DocumentUpdateVerification.cs'),
    (Join-Path $projectRoot 'addin\PageEditPlanning.cs'),
    (Join-Path $projectRoot 'addin\PageEditService.cs'),
    (Join-Path $projectRoot 'addin\PageScopeService.cs'),
    (Join-Path $projectRoot 'addin\SelectedPageEditContext.cs'),
    (Join-Path $projectRoot 'addin\PageTextCoverage.cs'),
    (Join-Path $projectRoot 'addin\PageUpdateWorkflow.cs'),
    (Join-Path $projectRoot 'addin\TableCursorContext.cs'),
    (Join-Path $projectRoot 'addin\TableStyleTransaction.cs'),
    (Join-Path $projectRoot 'addin\TableConcurrentText.cs'),
    (Join-Path $projectRoot 'addin\NativeTableStyleActions.cs'),
    (Join-Path $projectRoot 'addin\NativeTableBorderParameters.cs'),
    (Join-Path $projectRoot 'addin\ChatWorkspace.cs'),
    (Join-Path $projectRoot 'addin\ChatTheme.cs'),
    (Join-Path $projectRoot 'addin\AiRequestPopup.cs'),
    (Join-Path $projectRoot 'addin\manifest.xml')
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) { throw "Missing input: $requiredFile" }
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$bridgeOutput = Join-Path $outputRoot 'bridge'
$vendorOutput = Join-Path $bridgeOutput 'vendor'
New-Item -ItemType Directory -Path $vendorOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\hwp-codex-host.mjs') -Destination $bridgeOutput
Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\editor-host.mjs') -Destination $bridgeOutput
foreach ($richBridgeFile in @('rich-plan.mjs','codex-image-host.mjs','hwpx-fragment.mjs','table-design-plan.mjs','page-edit-plan.mjs','document-target-discovery.mjs')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot ('bridge\' + $richBridgeFile)) -Destination $bridgeOutput
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\templates') -Destination $bridgeOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\smoke.mjs') -Destination $bridgeOutput
Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\editor-smoke.mjs') -Destination $bridgeOutput
$pinnedModule = Join-Path $vendorOutput 'stoom-codex-native-host.mjs'
Copy-Item -LiteralPath $StoomModule -Destination $pinnedModule
Copy-Item -LiteralPath (Join-Path $projectRoot 'addin\manifest.xml') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'addin\assets') -Destination $outputRoot -Recurse -Force
$references = @(
    'System.dll', 'System.Core.dll', 'System.Web.Extensions.dll', 'System.Xaml.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll',
    'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll'
) | ForEach-Object { Join-Path $frameworkRoot $_ }
$references += Join-Path $HancomBin 'Hnc.Foundation.Interface.dll'
$references += Join-Path $HancomBin 'Hnc.Foundation.InternalInterface.dll'
$references += Join-Path $HancomBin 'HwpAddonHelper.dll'
$arguments = @('/nologo', '/target:library', '/platform:x86', '/optimize+', '/langversion:5',
    ('/win32icon:' + (Join-Path $projectRoot 'addin\assets\stoom.ico')),
    ('/out:' + (Join-Path $outputRoot 'Stoom.Hwp.Codex.Context.dll')))
$arguments += $references | ForEach-Object { '/reference:' + $_ }
$arguments += Join-Path $projectRoot 'addin\StoomCodex.cs'
$arguments += Join-Path $projectRoot 'addin\SelectionMenu.cs'
$arguments += Join-Path $projectRoot 'addin\NativeEditor.cs'
$arguments += Join-Path $projectRoot 'addin\SelectionEditor.cs'
$arguments += Join-Path $projectRoot 'addin\FrameRoute.cs'
$arguments += Join-Path $projectRoot 'addin\CaretAnchor.cs'
$arguments += Join-Path $projectRoot 'addin\ConversationIntent.cs'
$arguments += Join-Path $projectRoot 'addin\NativeComposer.cs'
$arguments += Join-Path $projectRoot 'addin\NativeRichComposer.cs'
$arguments += Join-Path $projectRoot 'addin\TableStylePlan.cs'
$arguments += Join-Path $projectRoot 'addin\TableStyleBatchPlanner.cs'
$arguments += Join-Path $projectRoot 'addin\TableDesignSnapshot.cs'
$arguments += Join-Path $projectRoot 'addin\TableDesignCompiler.cs'
$arguments += Join-Path $projectRoot 'addin\TableDesignService.cs'
$arguments += Join-Path $projectRoot 'addin\TableStableSnapshot.cs'
$arguments += Join-Path $projectRoot 'addin\NativePageContext.cs'
$arguments += Join-Path $projectRoot 'addin\ResponsivePageReader.cs'
$arguments += Join-Path $projectRoot 'addin\NativeResponsivePageSource.cs'
$arguments += Join-Path $projectRoot 'addin\NativePageIndicator.cs'
$arguments += Join-Path $projectRoot 'addin\NativePagePositionEvidence.cs'
$arguments += Join-Path $projectRoot 'addin\NativePageMapping.cs'
$arguments += Join-Path $projectRoot 'addin\NativeDocumentScan.cs'
$arguments += Join-Path $projectRoot 'addin\NativeDocumentInspection.cs'
$arguments += Join-Path $projectRoot 'addin\NativeDocumentUpdateBackend.cs'
$arguments += Join-Path $projectRoot 'addin\DocumentStructure.cs'
$arguments += Join-Path $projectRoot 'addin\DocumentTargetDiscovery.cs'
$arguments += Join-Path $projectRoot 'addin\DocumentTargetDiscoveryService.cs'
$arguments += Join-Path $projectRoot 'addin\DocumentTargetAnchors.cs'
$arguments += Join-Path $projectRoot 'addin\OnDemandTargetLocator.cs'
$arguments += Join-Path $projectRoot 'addin\NativeRepeatFindProbe.cs'
$arguments += Join-Path $projectRoot 'addin\HwpParagraphProofs.cs'
$arguments += Join-Path $projectRoot 'addin\AutomaticPageUpdate.cs'
$arguments += Join-Path $projectRoot 'addin\NativeAutomaticPageUpdateEnvironment.cs'
$arguments += Join-Path $projectRoot 'addin\TextOnlyAutomaticPageEvidence.cs'
$arguments += Join-Path $projectRoot 'addin\DocumentAddressMap.cs'
$arguments += Join-Path $projectRoot 'addin\DocumentUpdateTransaction.cs'
$arguments += Join-Path $projectRoot 'addin\DocumentUpdateVerification.cs'
$arguments += Join-Path $projectRoot 'addin\PageEditPlanning.cs'
$arguments += Join-Path $projectRoot 'addin\PageEditService.cs'
$arguments += Join-Path $projectRoot 'addin\PageScopeService.cs'
$arguments += Join-Path $projectRoot 'addin\SelectedPageEditContext.cs'
$arguments += Join-Path $projectRoot 'addin\PageTextCoverage.cs'
$arguments += Join-Path $projectRoot 'addin\PageUpdateWorkflow.cs'
$arguments += Join-Path $projectRoot 'addin\TableCursorContext.cs'
$arguments += Join-Path $projectRoot 'addin\TableStyleTransaction.cs'
$arguments += Join-Path $projectRoot 'addin\TableConcurrentText.cs'
$arguments += Join-Path $projectRoot 'addin\NativeTableStyleActions.cs'
$arguments += Join-Path $projectRoot 'addin\NativeTableBorderParameters.cs'
$arguments += Join-Path $projectRoot 'addin\ChatWorkspace.cs'
$arguments += Join-Path $projectRoot 'addin\ChatTheme.cs'
$arguments += Join-Path $projectRoot 'addin\AiRequestPopup.cs'
& $cscPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Add-in compilation failed.' }
$builtAssembly = Join-Path $outputRoot 'Stoom.Hwp.Codex.Context.dll'
[xml]$builtManifest = Get-Content -LiteralPath (Join-Path $outputRoot 'manifest.xml') -Raw
if (([Reflection.AssemblyName]::GetAssemblyName($builtAssembly)).Version.ToString() -ne $builtManifest.HncAddonInfo.Version.value) {
    throw 'Manifest and assembly versions do not match.'
}
$config = [ordered]@{
    NodePath = [IO.Path]::GetFullPath($NodePath)
    HostPath = Join-Path $bridgeOutput 'editor-host.mjs'
    StoomModulePath = $pinnedModule
}
$config | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'hwp-codex.json') -Encoding utf8
$provenance = [ordered]@{
    sourcePath = [IO.Path]::GetFullPath($StoomModule)
    sourceSha256 = (Get-FileHash -LiteralPath $StoomModule -Algorithm SHA256).Hash.ToLowerInvariant()
    pinnedSha256 = (Get-FileHash -LiteralPath $pinnedModule -Algorithm SHA256).Hash.ToLowerInvariant()
    sourceModified = (Get-Item -LiteralPath $StoomModule).LastWriteTimeUtc.ToString('o')
    capturedAt = [DateTime]::UtcNow.ToString('o')
    copiedUnmodified = $true
    usedExports = @('CodexConnection', 'discoverCodex', 'reviewFilesystem', 'APP_SERVER_ARGS')
    note = 'Local reference snapshot. The Chrome entry point is not executed. Hancom DLLs are resolved from the installed HWP host and are not redistributed.'
}
$provenance | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $vendorOutput 'provenance.json') -Encoding utf8
[PSCustomObject]@{ OutputDirectory = $outputRoot; Assembly = (Join-Path $outputRoot 'Stoom.Hwp.Codex.Context.dll'); SourceSha256 = $provenance.sourceSha256 }
