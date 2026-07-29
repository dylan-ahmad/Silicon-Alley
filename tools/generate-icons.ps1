# Silicon Alley icon authoring pipeline (issue #145).
#
# Turns tools/icon-manifest.txt rows (<stem> <author>/<name>) into white-on-transparent 128px sprite
# PNGs in Assets/Mods/SiliconAlley/UI/Icons/, each with a Unity .meta cloned from the canonical icon
# importer template (fresh GUID) so no Unity step is needed. Sources are the game-icons.net SVGs
# mirrored at github.com/game-icons/icons (CC BY 3.0 — every icon MUST get a CREDITS.md row).
#
# Usage:
#   .\tools\generate-icons.ps1                # generate anything missing (idempotent; skips existing PNGs)
#   .\tools\generate-icons.ps1 -Force -Only stat_eta   # regenerate one stem (after a manifest swap)
#   .\tools\generate-icons.ps1 -Verify        # no writes: format/whiteness/coverage + demanded-stem +
#                                             # CREDITS cross-checks; nonzero exit on any failure
#
# Rasterizer: a pinned resvg release, downloaded on demand into the gitignored tools/.cache/.
# (Fallbacks if that download is ever blocked: Inkscape CLI or `npx sharp` — not implemented here.)

[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$Verify,
    [string]$Only
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoRoot  = Split-Path -Parent $PSScriptRoot
$iconsDir  = Join-Path $repoRoot 'Assets\Mods\SiliconAlley\UI\Icons'
$cacheDir  = Join-Path $PSScriptRoot '.cache'
$manifest  = Join-Path $PSScriptRoot 'icon-manifest.txt'
$creditsMd = Join-Path $repoRoot 'CREDITS.md'

$resvgZipUrl = 'https://github.com/linebender/resvg/releases/download/v0.47.0/resvg-win64.zip'
$rawSvgBase  = 'https://raw.githubusercontent.com/game-icons/icons/master'

# Every stem the UI can request that must resolve without hitting the null tier of IconFor
# (the 29 manifest stems are checked from the manifest itself; these are the extra procedural ones).
$requiredCatStems = @(
    'cat_businesstype', 'cat_feature', 'cat_phase', 'cat_platform', 'cat_projecttype', 'cat_segment',
    'cat_tool', 'cat_stat', 'cat_ms', 'cat_publisher', 'cat_dep', 'cat_server'
)

# The canonical icon importer settings (byte-identical across the shipped icon set; only the guid
# differs per file). Keep in sync with SiliconAlleyIconPlaceholderGenerator's importer block.
$metaTemplate = @'
fileFormatVersion: 2
guid: __GUID__
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 0
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: Standalone
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName: siliconalley
  assetBundleVariant: unity3d
'@

function Read-Manifest {
    $rows = @()
    foreach ($line in Get-Content $manifest) {
        $t = $line.Trim()
        if ($t -eq '' -or $t.StartsWith('#')) { continue }
        $parts = $t -split '\s+'
        if ($parts.Count -ne 2 -or $parts[1] -notmatch '^[a-z0-9-]+/[a-z0-9-]+$') {
            throw "Bad manifest row: '$line'"
        }
        $rows += [pscustomobject]@{ Stem = $parts[0]; Source = $parts[1] }
    }
    return $rows
}

function Ensure-Resvg {
    $exe = Get-ChildItem -Path $cacheDir -Filter 'resvg.exe' -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($exe) { return $exe.FullName }
    New-Item -ItemType Directory -Force $cacheDir | Out-Null
    $zip = Join-Path $cacheDir 'resvg-win64.zip'
    Write-Host "Downloading resvg ($resvgZipUrl)..."
    Invoke-WebRequest -Uri $resvgZipUrl -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $cacheDir -Force
    $exe = Get-ChildItem -Path $cacheDir -Filter 'resvg.exe' -Recurse | Select-Object -First 1
    if (-not $exe) { throw 'resvg.exe not found after expanding the release zip.' }
    return $exe.FullName
}

# Force every glyph white: drop existing fill attributes and any full-canvas background path, then
# stamp fill="#ffffff" onto each <path>. (Repo SVGs are normally a single unfilled 512-viewBox path.)
function Convert-ToWhiteSvg([string]$svgText) {
    $s = $svgText -replace '<path[^>]*d="M0 0h512v512H0z"[^>]*/>', ''
    $s = $s -replace "fill=""[^""]*""", ''
    $s = $s -replace "fill='[^']*'", ''
    $s = $s -replace '<path', '<path fill="#ffffff"'
    return $s
}

function Test-IconPng([string]$pngPath, [ref]$failures) {
    $name = Split-Path $pngPath -Leaf
    $bytes = [IO.File]::ReadAllBytes($pngPath)
    if ($bytes.Length -lt 33) { $failures.Value += "$name : truncated file"; return }
    $w = ($bytes[16] -shl 24) + ($bytes[17] -shl 16) + ($bytes[18] -shl 8) + $bytes[19]
    $h = ($bytes[20] -shl 24) + ($bytes[21] -shl 16) + ($bytes[22] -shl 8) + $bytes[23]
    if ($w -ne 128 -or $h -ne 128) { $failures.Value += "$name : ${w}x${h}, expected 128x128" }
    if ($bytes[24] -ne 8) { $failures.Value += "$name : bit depth $($bytes[24]), expected 8" }
    if ($bytes[25] -ne 6) { $failures.Value += "$name : color type $($bytes[25]), expected 6 (RGBA)" }

    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap($pngPath)
    try {
        $opaque = 0
        $offWhite = 0
        for ($y = 0; $y -lt $bmp.Height; $y++) {
            for ($x = 0; $x -lt $bmp.Width; $x++) {
                $p = $bmp.GetPixel($x, $y)
                if ($p.A -gt 0) {
                    $opaque++
                    if ($p.R -lt 250 -or $p.G -lt 250 -or $p.B -lt 250) { $offWhite++ }
                }
            }
        }
        if ($opaque -eq 0) { $failures.Value += "$name : fully transparent (blank render)" }
        if ($offWhite -gt 0) { $failures.Value += "$name : $offWhite non-white visible pixel(s)" }
    }
    finally { $bmp.Dispose() }
}

$rows = Read-Manifest
if ($Only) {
    $rows = @($rows | Where-Object { $_.Stem -eq $Only })
    if ($rows.Count -eq 0) { throw "Stem '$Only' not in the manifest." }
}

$failures = @()

if ($Verify) {
    Write-Host "Verifying $($rows.Count) manifest icon(s) + $($requiredCatStems.Count) category stems..."
    foreach ($row in $rows) {
        $png = Join-Path $iconsDir "$($row.Stem).png"
        if (-not (Test-Path $png)) { $failures += "$($row.Stem) : PNG missing"; continue }
        if (-not (Test-Path "$png.meta")) { $failures += "$($row.Stem) : .meta missing" }
        Test-IconPng $png ([ref]$failures)
        $creditsRow = "``$($row.Stem)``"
        if (-not (Select-String -Path $creditsMd -SimpleMatch $creditsRow -Quiet)) {
            $failures += "$($row.Stem) : no CREDITS.md row"
        }
    }
    foreach ($stem in $requiredCatStems) {
        $png = Join-Path $iconsDir "$stem.png"
        if (-not (Test-Path $png)) { $failures += "$stem : placeholder PNG missing"; continue }
        if (-not (Test-Path "$png.meta")) { $failures += "$stem : placeholder .meta missing" }
    }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "FAIL  $_" }
        exit 1
    }
    Write-Host 'Verify: ALL PASS'
    exit 0
}

$resvg = Ensure-Resvg
New-Item -ItemType Directory -Force (Join-Path $cacheDir 'svg') | Out-Null
$made = 0
foreach ($row in $rows) {
    $png = Join-Path $iconsDir "$($row.Stem).png"
    if ((Test-Path $png) -and -not $Force) { continue }

    $svgUrl = "$rawSvgBase/$($row.Source).svg"
    $rawSvg = Join-Path $cacheDir ("svg\" + ($row.Source -replace '/', '__') + '.svg')
    try {
        Invoke-WebRequest -Uri $svgUrl -OutFile $rawSvg -UseBasicParsing
    }
    catch {
        $failures += "$($row.Stem) : fetch failed for $svgUrl ($($_.Exception.Message))"
        continue
    }

    $whiteSvg = Join-Path $cacheDir ("svg\" + ($row.Source -replace '/', '__') + '.white.svg')
    Set-Content -Path $whiteSvg -Value (Convert-ToWhiteSvg (Get-Content $rawSvg -Raw)) -Encoding utf8

    & $resvg -w 128 -h 128 $whiteSvg $png
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $png)) {
        $failures += "$($row.Stem) : resvg failed (exit $LASTEXITCODE)"
        continue
    }

    $meta = "$png.meta"
    if (-not (Test-Path $meta)) {
        $guid = [Guid]::NewGuid().ToString('N')
        Set-Content -Path $meta -Value $metaTemplate.Replace('__GUID__', $guid) -Encoding ascii
    }
    Write-Host "  made  $($row.Stem)  <=  $($row.Source)"
    $made++
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL  $_" }
    exit 1
}
Write-Host "Done: $made icon(s) generated. Run with -Verify for the full QA sweep."
