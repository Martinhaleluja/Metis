[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [string]$InnoCompilerPath
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'

$installerDir = $PSScriptRoot
$root = [System.IO.Path]::GetFullPath((Join-Path $installerDir '..'))
$publishDir = Join-Path $root 'artifacts\installer-publish'
$outputDir = Join-Path $installerDir 'output'
$generatedDir = Join-Path $installerDir '.generated'
$scriptPath = Join-Path $installerDir 'Metis.iss'
$localDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue

if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
} elseif ($dotnetCommand) {
    $dotnet = $dotnetCommand.Source
} else {
    throw 'The .NET 8 SDK is required to build Metis.'
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $compilerCandidates = @(
        (Join-Path $installerDir '.tools\Inno Setup 7\ISCC.exe'),
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $InnoCompilerPath = $compilerCandidates |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or
    -not (Test-Path -LiteralPath $InnoCompilerPath)) {
    throw 'ISCC.exe was not found. Install Inno Setup 7 or pass -InnoCompilerPath.'
}

function New-MetisIcon {
    param([Parameter(Mandatory)][string]$Path)

    Add-Type -AssemblyName System.Drawing
    if (-not ('MetisInstaller.NativeMethods' -as [type])) {
        Add-Type @'
namespace MetisInstaller
{
    using System;
    using System.Runtime.InteropServices;

    public static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
'@
    }

    $bitmap = [System.Drawing.Bitmap]::new(256, 256)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $pathShape = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 82, 211, 226))
    $icon = $null
    $handle = [IntPtr]::Zero
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $pathShape.StartFigure()
        $pathShape.AddBezier(128, 50, 102, 50, 98, 28, 68, 28)
        $pathShape.AddBezier(68, 28, 38, 28, 20, 52, 20, 84)
        $pathShape.AddBezier(20, 84, 20, 106, 42, 110, 42, 128)
        $pathShape.AddBezier(42, 128, 42, 146, 20, 150, 20, 172)
        $pathShape.AddBezier(20, 172, 20, 204, 44, 228, 76, 228)
        $pathShape.AddBezier(76, 228, 104, 228, 106, 206, 128, 206)
        $pathShape.AddBezier(128, 206, 150, 206, 152, 228, 180, 228)
        $pathShape.AddBezier(180, 228, 212, 228, 236, 204, 236, 172)
        $pathShape.AddBezier(236, 172, 236, 150, 214, 146, 214, 128)
        $pathShape.AddBezier(214, 128, 214, 110, 236, 106, 236, 84)
        $pathShape.AddBezier(236, 84, 236, 52, 218, 28, 188, 28)
        $pathShape.AddBezier(188, 28, 158, 28, 154, 50, 128, 50)
        $pathShape.CloseFigure()
        $graphics.FillPath($brush, $pathShape)

        $handle = $bitmap.GetHicon()
        $icon = [System.Drawing.Icon]::FromHandle($handle)
        $stream = [System.IO.File]::Create($Path)
        try {
            $icon.Save($stream)
        } finally {
            $stream.Dispose()
        }
    } finally {
        if ($icon) { $icon.Dispose() }
        if ($handle -ne [IntPtr]::Zero) { [MetisInstaller.NativeMethods]::DestroyIcon($handle) | Out-Null }
        $brush.Dispose()
        $pathShape.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

if (Test-Path -LiteralPath $publishDir) {
    $resolvedPublishDir = (Resolve-Path -LiteralPath $publishDir).Path
    $expectedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
    if (-not $resolvedPublishDir.StartsWith(
            $expectedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean publish directory outside artifacts: $resolvedPublishDir"
    }

    Remove-Item -LiteralPath $resolvedPublishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir, $outputDir, $generatedDir -Force | Out-Null
$iconPath = Join-Path $generatedDir 'Metis.ico'
New-MetisIcon -Path $iconPath

$assemblyVersion = "$Version.0"

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

    & $dotnet publish .\src\Metis.App\Metis.App.csproj `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDir `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:Version=$Version `
        -p:AssemblyVersion=$assemblyVersion `
        -p:FileVersion=$assemblyVersion `
        -p:InformationalVersion=$Version `
        -p:ApplicationIcon=$iconPath
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
} finally {
    Pop-Location
}

& $InnoCompilerPath `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishDir" `
    "/DInstallerIcon=$iconPath" `
    $scriptPath
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$setupPath = Join-Path $outputDir "Metis-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Expected setup file was not created: $setupPath"
}

$setup = Get-Item -LiteralPath $setupPath
Write-Host "Created $($setup.FullName) ($([Math]::Round($setup.Length / 1MB, 1)) MiB)"
