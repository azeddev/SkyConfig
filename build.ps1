[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue

if (-not $dotnetCommand) {
    throw '.NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0'
}
$dotnet = $dotnetCommand.Source
$installedSdks = & $dotnet --list-sdks
if (-not ($installedSdks | Select-String -Pattern '^8\.0\.')) {
    throw '.NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0'
}

Push-Location $root
try {
    if (-not $SkipTests) {
        & $dotnet run --project tests\SkyConfig.Core.Tests\SkyConfig.Core.Tests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    }

    $publishDirectory = Join-Path $root 'dist'
    $resolvedRoot = [System.IO.Path]::GetFullPath($root).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedPublish = [System.IO.Path]::GetFullPath($publishDirectory)
    if (-not $resolvedPublish.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear a publish directory outside the workspace: $resolvedPublish"
    }
    if (Test-Path -LiteralPath $resolvedPublish) {
        Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
    }

    & $dotnet publish src\SkyConfig\SkyConfig.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -o $resolvedPublish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    Copy-Item README.md, LICENSE, THIRD_PARTY_NOTICES.md -Destination dist -Force
    Write-Host "Published: $(Join-Path $root 'dist\SkyConfig.exe')"
} finally {
    Pop-Location
}
