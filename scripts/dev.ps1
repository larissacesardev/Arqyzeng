[CmdletBinding()]
param(
    [ValidateSet('Checks', 'Build', 'Install')]
    [string]$Action = 'Checks',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$RevitRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pluginProject = Join-Path $projectRoot 'src\Arqyzeng.Revit\Arqyzeng.Revit.csproj'
$checksProject = Join-Path $projectRoot 'tests\Arqyzeng.Rendering.Checks\Arqyzeng.Rendering.Checks.csproj'
$requestedSdk = (Get-Content -LiteralPath (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet nao encontrado. Este projeto exige o SDK $requestedSdk conforme global.json."
}
$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $sdkOutput = & dotnet --version 2>&1
    $sdkExitCode = $LASTEXITCODE
}
finally { $ErrorActionPreference = $previousErrorActionPreference }
if ($sdkExitCode -ne 0) {
    throw "SDK $requestedSdk indisponivel neste computador. Confira 'dotnet --list-sdks' e global.json antes de executar checks ou build."
}
$sdkVersion = [version](($sdkOutput | Out-String).Trim())
if ($sdkVersion.Major -ne 8 -or $sdkVersion.Minor -ne 0 -or $sdkVersion -lt [version]$requestedSdk) {
    throw "SDK selecionado: $sdkVersion. Necessario: $requestedSdk ou patch 8.0 posterior compativel com global.json."
}

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Falhou: dotnet $($Arguments -join ' ') (codigo $LASTEXITCODE). A instalacao nao foi iniciada."
    }
}

Write-Host "Raiz: $projectRoot"
Write-Host "SDK: $sdkVersion"
Invoke-DotNet -Arguments @('run', '--project', $checksProject, '--configuration', $Configuration, '--no-launch-profile')
Write-Host 'Checks independentes da API Revit concluidos.'
if ($Action -eq 'Checks') { return }

if ([string]::IsNullOrWhiteSpace($RevitRoot)) { $RevitRoot = $env:REVIT_2026_DIR }
if ([string]::IsNullOrWhiteSpace($RevitRoot)) { $RevitRoot = 'C:\Program Files\Autodesk\Revit 2026' }
$RevitRoot = [IO.Path]::GetFullPath($RevitRoot)
foreach ($name in @('Revit.exe', 'RevitAPI.dll', 'RevitAPIUI.dll')) {
    $file = Join-Path $RevitRoot $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Build do plugin bloqueado: referencia do Revit 2026 ausente em $file. Execute Build ou Install no computador com Revit 2026; isso nao indica defeito do codigo. Defina REVIT_2026_DIR ou passe -RevitRoot se necessario."
    }
}
$revitVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $RevitRoot 'Revit.exe'))
if ($revitVersion.FileMajorPart -ne 26 -or $revitVersion.FileMinorPart -ge 5) {
    throw "Versao do Revit incompativel com este alvo .NET 8: $($revitVersion.FileVersion). Este fluxo aceita Revit 2026 anterior a 2026.5, como os instaladores legados."
}
Write-Host "Referencias do Revit: $RevitRoot"

Invoke-DotNet -Arguments @('build', $pluginProject, '--configuration', $Configuration, ("-p:RevitInstallDir=$RevitRoot"))
if ($Action -eq 'Build') { return }

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Feche o Revit antes de substituir a instalacao do plugin.'
}
if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA) -or [string]::IsNullOrWhiteSpace($env:APPDATA)) {
    throw 'LOCALAPPDATA e APPDATA sao necessarios para instalar o add-in por usuario.'
}

$outputDir = Join-Path $projectRoot ".artifacts\bin\Arqyzeng.Revit\$Configuration\net8.0-windows"
$outputDll = Join-Path $outputDir 'Arqyzeng.Revit.dll'
$outputData = Join-Path $outputDir 'data'
if (-not (Test-Path -LiteralPath $outputDll -PathType Leaf)) {
    throw "Build sem DLL esperada: $outputDll. A instalacao nao foi iniciada."
}
foreach ($name in @('BumpDayRules.json', 'BumpNightRules.json', 'RenderGeneralRules.json', 'MaterialLibrary.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $outputData $name) -PathType Leaf)) {
        throw "Build sem regra necessaria: $name. A instalacao nao foi iniciada."
    }
}

$installRoot = Join-Path $env:LOCALAPPDATA 'ArqyzengBIM'
$installDir = Join-Path $installRoot 'Revit2026'
$manifestDir = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026'
$manifestPath = Join-Path $manifestDir 'ArqyzengBIM.addin'
$id = (Get-Date -Format 'yyyyMMdd_HHmmss') + '_' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$stageDir = Join-Path $installRoot ('.staging-' + $id)
$backupDir = Join-Path $installRoot ('install-backups\' + $id)
$backupPlugin = Join-Path $backupDir 'Revit2026'
$backupManifest = Join-Path $backupDir 'ArqyzengBIM.addin'
$stagedManifest = Join-Path $manifestDir ('.ArqyzengBIM-' + $id + '.addin.tmp')

New-Item -ItemType Directory -Path $stageDir, $manifestDir, $backupDir -Force | Out-Null
foreach ($entry in Get-ChildItem -LiteralPath $outputDir -Force) {
    Copy-Item -LiteralPath $entry.FullName -Destination $stageDir -Recurse -Force
}
if (-not (Test-Path -LiteralPath (Join-Path $stageDir 'Arqyzeng.Revit.dll') -PathType Leaf)) {
    throw 'A DLL nao foi copiada para a pasta temporaria. A instalacao existente permanece intacta.'
}
foreach ($name in @('BumpDayRules.json', 'BumpNightRules.json', 'RenderGeneralRules.json', 'MaterialLibrary.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $stageDir 'data') $name) -PathType Leaf)) {
        throw "A regra $name nao foi copiada para a pasta temporaria. A instalacao existente permanece intacta."
    }
}

$assemblyPath = Join-Path $installDir 'Arqyzeng.Revit.dll'
$escapedAssembly = [System.Security.SecurityElement]::Escape($assemblyPath)
$manifestXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Arqyzeng BIM</Name>
    <Assembly>$escapedAssembly</Assembly>
    <AddInId>46d7fa3e-485c-488e-b9e6-62f705927db6</AddInId>
    <FullClassName>Arqyzeng.Revit.App</FullClassName>
    <VendorId>ARQY</VendorId>
    <VendorDescription>Arqyzeng BIM - MVP local</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
[IO.File]::WriteAllText($stagedManifest, $manifestXml, (New-Object System.Text.UTF8Encoding($false)))

$oldPluginMoved = $false
$oldManifestMoved = $false
$newPluginInstalled = $false
$newManifestInstalled = $false
try {
    if (Test-Path -LiteralPath $installDir) {
        Move-Item -LiteralPath $installDir -Destination $backupPlugin
        $oldPluginMoved = $true
    }
    if (Test-Path -LiteralPath $manifestPath) {
        Move-Item -LiteralPath $manifestPath -Destination $backupManifest
        $oldManifestMoved = $true
    }
    Move-Item -LiteralPath $stageDir -Destination $installDir
    $newPluginInstalled = $true
    Move-Item -LiteralPath $stagedManifest -Destination $manifestPath
    $newManifestInstalled = $true
}
catch {
    $installError = $_
    try {
        if ($newManifestInstalled -and (Test-Path -LiteralPath $manifestPath)) {
            Move-Item -LiteralPath $manifestPath -Destination (Join-Path $backupDir 'failed.addin')
        }
        if ($newPluginInstalled -and (Test-Path -LiteralPath $installDir)) {
            Move-Item -LiteralPath $installDir -Destination (Join-Path $backupDir 'failed-plugin')
        }
        if ($oldPluginMoved) { Move-Item -LiteralPath $backupPlugin -Destination $installDir }
        if ($oldManifestMoved) { Move-Item -LiteralPath $backupManifest -Destination $manifestPath }
    }
    catch {
        throw "Instalacao falhou e a restauracao automatica tambem falhou. Confira $backupDir. Causa: $installError. Restauracao: $_"
    }
    throw "Instalacao falhou; a instalacao anterior foi restaurada. Backups: $backupDir. Causa: $installError"
}

Write-Host "Plugin instalado: $assemblyPath"
Write-Host "Manifesto: $manifestPath"
Write-Host "Backup anterior: $backupDir"
