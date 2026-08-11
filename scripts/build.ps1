[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue

# Prefer Metis's portable SDK. A machine can have dotnet.exe on PATH with only
# an old runtime installed, which looks valid until restore/build fails.
if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
} elseif ($dotnetCommand) {
    $dotnet = $dotnetCommand.Source
} else {
    throw 'The .NET 8 SDK is required. Install Microsoft.DotNet.SDK.8 or place a portable SDK in %USERPROFILE%\.dotnet.'
}

Push-Location $root
try {
    & $dotnet restore .\Metis.sln --disable-parallel -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    & $dotnet build .\Metis.sln --configuration $Configuration --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    if (-not $SkipTests) {
        & $dotnet test .\Metis.sln --configuration $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
    }

    if ($Publish) {
        $output = Join-Path $root 'artifacts\win-x64'

        # dotnet publish adds to the output directory without clearing it, so a
        # renamed or removed assembly would linger next to the current build and
        # could still be launched or shipped. Clear it first, guarding against a
        # path that somehow resolves outside artifacts.
        if (Test-Path -LiteralPath $output) {
            $resolvedOutput = (Resolve-Path -LiteralPath $output).Path
            $artifactsRoot = (Resolve-Path -LiteralPath (Join-Path $root 'artifacts')).Path
            if (-not $resolvedOutput.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to clean publish directory outside artifacts: $resolvedOutput"
            }
            Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
        }

        & $dotnet publish .\src\Metis.App\Metis.App.csproj `
            --configuration $Configuration `
            --runtime win-x64 `
            --self-contained true `
            --output $output `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
        Write-Host "Published Metis to $output"
    }
} finally {
    Pop-Location
}
