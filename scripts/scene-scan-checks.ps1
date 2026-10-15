param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$sceneRepo = Split-Path -Parent $PSScriptRoot
$sceneBuild = Join-Path $sceneRepo '.artifacts/scene-scan-checks'
[System.IO.Directory]::CreateDirectory($sceneBuild) | Out-Null
# Transient test project: the solution, plugin/test csproj files and global.json stay untouched.
$sceneSources = @('src/Arqyzeng.Revit/SceneScanModels.cs', 'src/Arqyzeng.Revit/SceneCameraMath.cs',
    'src/Arqyzeng.Revit/SceneScanSerializer.cs', 'src/Arqyzeng.Revit/CaptureModels.cs',
    'src/Arqyzeng.Revit/RenderSettings.cs', 'tests/SceneScanChecks.cs')
$sceneItems = foreach ($source in $sceneSources) {
    $scenePath = [System.Security.SecurityElement]::Escape((Join-Path $sceneRepo $source))
    '    <Compile Include="' + $scenePath + '" />'
}
$sceneProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
'@
$sceneProject += "`n" + ($sceneItems -join "`n") + "`n  </ItemGroup>`n</Project>`n"
$sceneProjectPath = Join-Path $sceneBuild 'SceneScan.Checks.csproj'
[System.IO.File]::WriteAllText($sceneProjectPath, $sceneProject, [System.Text.UTF8Encoding]::new($false))
Push-Location $sceneRepo
try {
    & dotnet run --project $sceneProjectPath --configuration $Configuration --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "Scene Scan checks failed (exit $LASTEXITCODE)." }
} finally { Pop-Location }
