$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\CodexQuotaTitle.Cli\CodexQuotaTitle.Cli.csproj'
$outputPath = Join-Path $repositoryRoot 'plugins\codex-quota-title\bin\win-x64'
$mcpProjectPath = Join-Path $repositoryRoot 'src\CodexQuotaTitle.Mcp\CodexQuotaTitle.Mcp.csproj'
$mcpOutputPath = Join-Path $repositoryRoot 'plugins\codex-quota-title\bin\mcp\win-x64'

dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

dotnet publish $mcpProjectPath --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output $mcpOutputPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish MCP failed with exit code $LASTEXITCODE"
}
