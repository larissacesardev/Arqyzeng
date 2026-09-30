[CmdletBinding()]
param(
    [string]$ProjectRoot = (Join-Path $env:USERPROFILE 'source\repos\ArqyzengBIM'),
    [string]$RevitRoot = 'C:\Program Files\Autodesk\Revit 2026'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Etapa inicial: Ribbon e comando de teste. Nao altera modelos RVT.
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Feche o Revit antes de compilar e instalar. Execute este script novamente depois.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'O comando dotnet nao foi encontrado. Abra um PowerShell novo apos instalar o SDK.'
}
$solution = Join-Path $ProjectRoot 'ArqyzengBIM.sln'
$projectDir = Join-Path $ProjectRoot 'src\Arqyzeng.Revit'
$projectFile = Join-Path $projectDir 'Arqyzeng.Revit.csproj'
if (-not (Test-Path $solution) -or -not (Test-Path $projectFile)) {
    throw "A solucao e o projeto existentes devem estar em: $ProjectRoot"
}
foreach ($name in @('RevitAPI.dll', 'RevitAPIUI.dll', 'Revit.exe')) {
    if (-not (Test-Path (Join-Path $RevitRoot $name))) {
        throw "Arquivo nao encontrado: $RevitRoot\$name"
    }
}
$revitVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $RevitRoot 'Revit.exe'))
if ($revitVersion.FileMajorPart -ne 26 -or $revitVersion.FileMinorPart -ge 5) {
    throw 'Este primeiro build usa .NET 8 e destina-se ao Revit 2026 anterior ao 2026.5.'
}

$utf8 = New-Object System.Text.UTF8Encoding($true)
function Write-SourceFile([string]$Path, [string]$Content) {
    [System.IO.File]::WriteAllText($Path, $Content, $utf8)
}

# Guarda os arquivos que este script vai atualizar, antes da escrita.
$backupDir = Join-Path $ProjectRoot ('backups\bootstrap-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
foreach ($name in @('Arqyzeng.Revit.csproj', 'App.cs', 'StartupTestCommand.cs', 'PluginLog.cs')) {
    $existing = Join-Path $projectDir $name
    if (Test-Path $existing) { Copy-Item $existing (Join-Path $backupDir $name) }
}

$escapedRevitRoot = [System.Security.SecurityElement]::Escape($RevitRoot)
$projectXml = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>Arqyzeng.Revit</AssemblyName>
    <RootNamespace>Arqyzeng.Revit</RootNamespace>
    <RevitInstallDir>__REVIT_ROOT__</RevitInstallDir>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="RevitAPI">
      <HintPath>$(RevitInstallDir)\RevitAPI.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="RevitAPIUI">
      <HintPath>$(RevitInstallDir)\RevitAPIUI.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
'@
Write-SourceFile $projectFile ($projectXml.Replace('__REVIT_ROOT__', $escapedRevitRoot))

$appSource = @'
using Autodesk.Revit.UI;

namespace Arqyzeng.Revit;

public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            const string tab = "Arqyzeng BIM";
            application.CreateRibbonTab(tab);
            RibbonPanel panel = application.CreateRibbonPanel(tab, "MVP");
            var buttonData = new PushButtonData(
                "Arqyzeng.StartupTest", "Testar\nplugin",
                typeof(App).Assembly.Location,
                typeof(StartupTestCommand).FullName!);
            buttonData.AvailabilityClassName = typeof(StartupTestAvailability).FullName!;
            var button = (PushButton)panel.AddItem(buttonData);
            button.ToolTip = "Verifica o carregamento do primeiro plugin Arqyzeng BIM.";
            PluginLog.Write("Plugin iniciado.");
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            PluginLog.Write("Falha no startup.", exception);
            TaskDialog.Show("Arqyzeng BIM", "Nao foi possivel iniciar o plugin. Consulte o log local.");
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        PluginLog.Write("Plugin encerrado.");
        return Result.Succeeded;
    }
}
'@
Write-SourceFile (Join-Path $projectDir 'App.cs') $appSource

$commandSource = @'
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Arqyzeng.Revit;

[Transaction(TransactionMode.Manual)]
public sealed class StartupTestCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            TaskDialog.Show("Arqyzeng BIM",
                "O plugin carregou e o comando foi executado com sucesso.\n\n" +
                "Revit: " + commandData.Application.Application.VersionNumber + "\n" +
                "Primeira etapa: Ribbon e integracao nativa.\n" +
                "Proxima etapa: interface Print e captura da vista 3D.");
            PluginLog.Write("Comando de teste executado.");
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            PluginLog.Write("Falha no comando de teste.", exception);
            message = "Nao foi possivel executar o teste. Consulte o log local.";
            return Result.Failed;
        }
    }
}

public sealed class StartupTestAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}
'@
Write-SourceFile (Join-Path $projectDir 'StartupTestCommand.cs') $commandSource

$logSource = @'
using System.IO;

namespace Arqyzeng.Revit;

internal static class PluginLog
{
    internal static void Write(string text, Exception? exception = null)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ArqyzengBIM", "logs");
            Directory.CreateDirectory(directory);
            // Somente mensagens tecnicas: sem dados do projeto, chaves ou tokens.
            string detail = exception is null ? string.Empty : " | " + exception.GetType().FullName;
            File.AppendAllText(Path.Combine(directory, "bootstrap.log"),
                DateTimeOffset.Now.ToString("O") + " | " + text + detail + Environment.NewLine);
        }
        catch
        {
            // Falhas no log nao devem impedir o carregamento do plugin.
        }
    }
}
'@
Write-SourceFile (Join-Path $projectDir 'PluginLog.cs') $logSource

# A pasta de backups fica fora do projeto C# e fora do versionamento.
$gitignore = Join-Path $ProjectRoot '.gitignore'
if (Test-Path $gitignore) {
    $ignoreContent = [System.IO.File]::ReadAllText($gitignore)
    if ($ignoreContent -notmatch '(?m)^/backups/\s*$') {
        [System.IO.File]::AppendAllText($gitignore, "`r`n/backups/`r`n", $utf8)
    }
}

Push-Location $ProjectRoot
try {
    $sdkVersion = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -notlike '8.*') {
        throw 'O projeto deve selecionar o SDK 8 pelo global.json antes da compilacao.'
    }
    & dotnet build $projectFile --configuration Debug
    if ($LASTEXITCODE -ne 0) {
        throw "A compilacao falhou. Nenhum novo build foi instalado. Backups: $backupDir"
    }
}
finally { Pop-Location }

$outputDir = Join-Path $projectDir 'bin\Debug\net8.0-windows'
$outputDll = Join-Path $outputDir 'Arqyzeng.Revit.dll'
if (-not (Test-Path $outputDll)) { throw 'A DLL compilada nao foi encontrada.' }
$installDir = Join-Path $env:LOCALAPPDATA 'ArqyzengBIM\Revit2026'
$manifestDir = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026'
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
Get-ChildItem -Path $outputDir -File | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $installDir $_.Name) -Force
}
$manifestPath = Join-Path $manifestDir 'ArqyzengBIM.addin'
if (Test-Path $manifestPath) { Copy-Item $manifestPath (Join-Path $backupDir 'ArqyzengBIM.addin') }
$escapedAssembly = [System.Security.SecurityElement]::Escape((Join-Path $installDir 'Arqyzeng.Revit.dll'))
$manifestXml = @'
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Arqyzeng BIM</Name>
    <Assembly>__ASSEMBLY_PATH__</Assembly>
    <AddInId>46d7fa3e-485c-488e-b9e6-62f705927db6</AddInId>
    <FullClassName>Arqyzeng.Revit.App</FullClassName>
    <VendorId>ARQY</VendorId>
    <VendorDescription>Arqyzeng BIM - MVP local</VendorDescription>
  </AddIn>
</RevitAddIns>
'@
Write-SourceFile $manifestPath ($manifestXml.Replace('__ASSEMBLY_PATH__', $escapedAssembly))
Write-Host ''
Write-Host 'INSTALACAO CONCLUIDA.' -ForegroundColor Green
Write-Host "DLL: $(Join-Path $installDir 'Arqyzeng.Revit.dll')"
Write-Host "Manifesto: $manifestPath"
Write-Host "Backups: $backupDir"
Write-Host 'Abra o Revit 2026.4.1 e clique em Arqyzeng BIM > Testar plugin.'
Write-Host 'Esta etapa nao altera o modelo e ainda nao implementa Print nem IA.'
