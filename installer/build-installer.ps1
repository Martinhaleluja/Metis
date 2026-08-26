[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '3.13.0',
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

function New-CompanionSilhouette {
    # The eight-bezier squircle the companion itself is drawn from. Kept
    # byte-identical to src\Metis.App\Branding\MetisIconFactory.cs and the
    # CompanionWindow Path data so the shortcut, the taskbar, the tray, and the
    # sprite on screen are recognisably one character rather than three.
    $shape = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $shape.StartFigure()
    $shape.AddBezier(34, 8, 42, -1, 56, -1, 64, 7)
    $shape.AddBezier(64, 7, 72, 15, 72, 27, 66, 35)
    $shape.AddBezier(66, 35, 72, 43, 72, 55, 64, 63)
    $shape.AddBezier(64, 63, 56, 71, 44, 71, 35, 65)
    $shape.AddBezier(35, 65, 27, 71, 15, 71, 7, 63)
    $shape.AddBezier(7, 63, -1, 55, -1, 43, 5, 35)
    $shape.AddBezier(5, 35, -1, 27, -1, 15, 7, 7)
    $shape.AddBezier(7, 7, 15, -1, 27, -1, 34, 8)
    $shape.CloseFigure()
    return $shape
}

function New-CompanionFramePng {
    param([Parameter(Mandatory)][int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $shape = New-CompanionSilhouette
    $payload = $null
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        # Fit from the path's real bounds rather than its nominal 0-72 design
        # box: several control points sit outside that box, and assuming the
        # box clips the silhouette's edges.
        $bounds = $shape.GetBounds()
        $margin = [double]$Size * 0.06
        $scale = ([double]$Size - ($margin * 2)) / [Math]::Max($bounds.Width, $bounds.Height)
        $transform = [System.Drawing.Drawing2D.Matrix]::new()
        $transform.Translate(
            [float]((($Size - ($bounds.Width * $scale)) / 2) - ($bounds.X * $scale)),
            [float]((($Size - ($bounds.Height * $scale)) / 2) - ($bounds.Y * $scale)))
        $transform.Scale([float]$scale, [float]$scale)
        $shape.Transform($transform)
        $transform.Dispose()

        $painted = $shape.GetBounds()
        $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new($painted.X, $painted.Y),
            [System.Drawing.PointF]::new($painted.Right, $painted.Bottom),
            [System.Drawing.Color]::FromArgb(255, 165, 225, 255),
            [System.Drawing.Color]::FromArgb(255, 70, 194, 255))
        $graphics.FillPath($gradient, $shape)
        $gradient.Dispose()

        # Gloss only on the larger frames. At 16 and 24 px a highlight muddies
        # the silhouette instead of shaping it, and the outline is all that
        # actually survives at that size.
        if ($Size -ge 48) {
            $previousClip = $graphics.Clip
            $graphics.SetClip($shape)
            $highlight = [System.Drawing.Drawing2D.GraphicsPath]::new()
            $highlight.AddEllipse(
                [float]($painted.X + ($painted.Width * 0.10)),
                [float]($painted.Y - ($painted.Height * 0.34)),
                [float]($painted.Width * 0.80),
                [float]($painted.Height * 0.72))
            $gloss = [System.Drawing.Drawing2D.PathGradientBrush]::new($highlight)
            $gloss.CenterColor = [System.Drawing.Color]::FromArgb(115, 255, 255, 255)
            $gloss.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
            $graphics.FillPath($gloss, $highlight)
            $gloss.Dispose()
            $highlight.Dispose()
            $graphics.Clip = $previousClip
        }

        $buffer = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($buffer, [System.Drawing.Imaging.ImageFormat]::Png)
            $payload = $buffer.ToArray()
        } finally {
            $buffer.Dispose()
        }
    } finally {
        $shape.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    # Returned through the comma operator: PowerShell unrolls an array on
    # output, so a bare "return $payload" would emit the byte[] one byte at a
    # time and the caller would reassemble it as an object[].
    return , $payload
}

function New-MetisIcon {
    param([Parameter(Mandatory)][string]$Path)

    Add-Type -AssemblyName System.Drawing

    # Every frame is drawn at its final size rather than downscaled from one
    # large bitmap, so 16 and 24 px stay crisp. The previous implementation
    # called Bitmap.GetHicon, which silently returns a single 32x32 frame no
    # matter how large the source bitmap is, leaving the shortcut blurry
    # everywhere Explorer asks for something bigger.
    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $frames = @()
    foreach ($size in $sizes) {
        $frames += , (New-CompanionFramePng -Size $size)
    }

    $stream = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)

        $offset = 6 + (16 * $sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $payload = $frames[$index]

            # A 256 px frame records its dimension as 0: the field is one byte.
            if ($size -ge 256) { $dimension = [byte]0 } else { $dimension = [byte]$size }

            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$payload.Length)
            $writer.Write([uint32]$offset)
            $offset += $payload.Length
        }

        foreach ($payload in $frames) {
            $writer.Write($payload)
        }
    } finally {
        $writer.Dispose()
        $stream.Dispose()
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
        -p:ApplicationIcon=$iconPath `
        -p:NuGetAudit=false
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
